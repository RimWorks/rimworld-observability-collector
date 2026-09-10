using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace RimWorks.RimObs.Collector.Api;

public static class SessionsEndpoints {
    private const int DefaultHotspotLimit = 50;
    private const int MaxHotspotLimit = 500;
    private const int DefaultGcEventLimit = 100;
    private const int MaxGcEventLimit = 1024;
    private const int MaxCallTreeDepth = 64;
    private const int MaxCallTreeTopN = 256;

    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/sessions", GetSessions);
        endpoints.MapGet("/api/v1/sessions/current", GetCurrentSession);
        endpoints.MapGet("/api/v1/sessions/current/summary", GetCurrentSummary);
        endpoints.MapGet("/api/v1/sessions/current/sections", GetCurrentSections);
        endpoints.MapGet("/api/v1/sessions/current/hotspots", GetCurrentHotspots);
        endpoints.MapGet("/api/v1/sessions/current/sections/{id:int}/timeseries", GetSectionTimeseries);
        endpoints.MapGet("/api/v1/sessions/current/gc", GetCurrentGc);
        endpoints.MapGet("/api/v1/sessions/current/metrics", GetCurrentMetrics);
        endpoints.MapGet("/api/v1/sessions/current/patches", GetCurrentPatches);
        endpoints.MapGet("/api/v1/sessions/current/call_tree", GetCurrentCallTree);
        endpoints.MapPost("/api/v1/sessions/{id}/name", RenameSession);
        endpoints.MapPost("/api/v1/sessions/new", StartNewSession);
        endpoints.MapPost("/api/v1/sessions/restart-game", RestartGame);
        endpoints.MapGet("/api/v1/sections", GetSections);
        return endpoints;
    }

    private static IResult GetSessions(SessionAggregator aggregator, IServiceProvider services) {
        SessionMeta? current = aggregator.Meta;
        List<object> sessions = new List<object>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        if (current is not null) {
            sessions.Add(MapSession(current, isCurrent: true, aggregator.SessionName));
            seen.Add(current.SessionId);
        }

        if (services.GetService<SqliteSessionPersister>() is { } persister) {
            foreach (StoredSession stored in SessionCatalog.List(persister.SessionsDirectory)) {
                if (seen.Add(stored.Meta.SessionId))
                    sessions.Add(MapSession(
                        stored.Meta,
                        isCurrent: stored.Meta.SessionId == current?.SessionId,
                        stored.Name));
            }
        }

        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            sessions = sessions.ToArray(),
        });
    }

    /// <summary>
    /// Labels a session. POST so the origin and bearer checks gate it. The live session is
    /// renamed in memory as well as on disk, so /status shows it before the next flush.
    /// </summary>
    private static async Task<IResult> RenameSession(
        HttpContext context,
        string id,
        SessionAggregator aggregator,
        IServiceProvider services) {
        (RenameRequest? body, IResult? error) = await RequestBody.Read<RenameRequest>(context, "session name");
        if (error is not null)
            return error;

        string name = body!.Name.Trim();
        if (name.Length > MaxSessionNameLength)
            return Results.BadRequest(new {
                schema_version = SchemaVersion.Current,
                reason = $"name must be {MaxSessionNameLength} characters or fewer",
            });

        bool isCurrent = aggregator.Meta?.SessionId == id;
        bool persisted = false;
        if (services.GetService<SqliteSessionPersister>() is { } persister)
            persisted = persister.WriteSessionName(id, name);

        if (isCurrent)
            aggregator.SessionName = name;

        if (!persisted && !isCurrent)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no such session" });

        return Results.Ok(new { schema_version = SchemaVersion.Current, id, name });
    }

    private sealed class RenameRequest {
        public string Name { get; init; } = string.Empty;
    }

    private const int MaxSessionNameLength = 80;

    /// <summary>
    /// Asks the game to start a fresh session. The collector cannot mint the id itself: the
    /// library owns the anchor every sample is timed against, so it has to re-anchor and then
    /// tell us the new id on the next SessionMeta burst.
    /// </summary>
    private static async Task<IResult> StartNewSession(SessionMetaRegistry registry) {
        if (!registry.IsAvailable)
            return Results.Problem(
                detail: "the game is not reachable, so a new session cannot be started",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        ControlClient client = new(registry.ControlPort, registry.ControlSecret);
        await client.NewSessionAsync();
        return Results.Ok(new { schema_version = SchemaVersion.Current });
    }

    /// <summary>
    /// Restarts the game so the next launch is a new session. The name is parked in config
    /// rather than applied now: the collector dies with the game, so it has to survive on disk
    /// and be claimed by whatever session comes up next.
    /// </summary>
    private static async Task<IResult> RestartGame(
        HttpContext context,
        SessionMetaRegistry registry,
        ConfigStore config) {
        (RestartRequest? body, IResult? error) = await RequestBody.Read<RestartRequest>(context, "restart");
        if (error is not null)
            return error;

        if (!registry.IsAvailable)
            return Results.Problem(
                detail: "the game is not reachable, so it cannot be restarted",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        string name = body!.Name.Trim();
        if (name.Length > MaxSessionNameLength)
            return Results.BadRequest(new {
                schema_version = SchemaVersion.Current,
                reason = $"name must be {MaxSessionNameLength} characters or fewer",
            });

        config.Current.Session.PendingName = name;
        config.Replace(config.Current);

        ControlClient client = new(registry.ControlPort, registry.ControlSecret);
        await client.RestartGameAsync(body.Save);
        return Results.Accepted(value: new { schema_version = SchemaVersion.Current, pending_name = name });
    }

    private sealed class RestartRequest {
        public string Name { get; init; } = string.Empty;
        public bool Save { get; init; } = true;
    }

    private static IResult GetCurrentSession(SessionAggregator aggregator) {
        SessionMeta? meta = aggregator.Meta;
        if (meta is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no active session" });

        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            session = MapSession(meta, isCurrent: true, aggregator.SessionName),
            receive = ReceiveCounters.Project(aggregator),
        });
    }

    private static IResult GetCurrentSummary(SessionAggregator aggregator) {
        SessionMeta? meta = aggregator.Meta;
        if (meta is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no active session" });

        double nsPerTick = NsPerTick(meta);
        long totalSectionTicks = 0;
        foreach (SectionStats section in aggregator.SnapshotSections())
            totalSectionTicks += section.TotalElapsedTicks;

        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            session = MapSession(meta, isCurrent: true, aggregator.SessionName),
            section_count = aggregator.SectionCount,
            metric_count = aggregator.MetricCount,
            total_batches = aggregator.TotalBatches,
            total_samples = aggregator.TotalSamples,
            total_bytes = aggregator.TotalBytes,
            total_gc_events = aggregator.TotalGcEvents,
            total_allocations = aggregator.TotalAllocations,
            total_metric_observations = aggregator.TotalMetricObservations,
            total_section_ns = (long)(totalSectionTicks * nsPerTick),
            last_batch_utc = aggregator.LastBatchUtc == default ? (DateTime?)null : aggregator.LastBatchUtc,
        });
    }

    private static IResult GetCurrentSections(SessionAggregator aggregator) {
        double nsPerTick = NsPerTick(aggregator.Meta);
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            sections = aggregator.SnapshotSections().Select(s => {
                PercentileSnapshot p = s.Distribution.SnapshotPercentiles();
                return new {
                    id = s.SectionId,
                    name = s.Name,
                    sample_count = s.SampleCount,
                    total_ns = (long)(s.TotalElapsedTicks * nsPerTick),
                    min_ns = s.MinElapsedTicks == long.MaxValue ? 0 : (long)(s.MinElapsedTicks * nsPerTick),
                    max_ns = (long)(s.MaxElapsedTicks * nsPerTick),
                    p50_ns = (long)(p.P50Ticks * nsPerTick),
                    p95_ns = (long)(p.P95Ticks * nsPerTick),
                    p99_ns = (long)(p.P99Ticks * nsPerTick),
                };
            }).ToArray(),
        });
    }

    private static IResult GetCurrentHotspots(SessionAggregator aggregator, int? limit) {
        double nsPerTick = NsPerTick(aggregator.Meta);
        int take = QueryLimit.Clamp(limit, DefaultHotspotLimit, MaxHotspotLimit);
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            hotspots = aggregator.SnapshotSections()
                .OrderByDescending(s => s.TotalElapsedTicks)
                .Take(take)
                .Select(s => {
                    PercentileSnapshot p = s.Distribution.SnapshotPercentiles();
                    return new {
                        id = s.SectionId,
                        name = s.Name,
                        subsystem = s.Subsystem,
                        sample_count = s.SampleCount,
                        total_ns = (long)(s.TotalElapsedTicks * nsPerTick),
                        mean_ns = s.SampleCount == 0 ? 0 : (long)(s.TotalElapsedTicks * nsPerTick / s.SampleCount),
                        min_ns = s.MinElapsedTicks == long.MaxValue ? 0 : (long)(s.MinElapsedTicks * nsPerTick),
                        max_ns = (long)(s.MaxElapsedTicks * nsPerTick),
                        p50_ns = (long)(p.P50Ticks * nsPerTick),
                        p95_ns = (long)(p.P95Ticks * nsPerTick),
                        p99_ns = (long)(p.P99Ticks * nsPerTick),
                    };
                })
                .ToArray(),
        });
    }

    private static IResult GetSectionTimeseries(SessionAggregator aggregator, int id) {
        SectionStats? stats = aggregator.FindSection(id);
        if (stats is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "unknown section" });

        double nsPerTick = NsPerTick(aggregator.Meta);
        long nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TimelineBucket[] buckets = stats.Distribution.SnapshotTimeline(nowEpoch);
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            id = stats.SectionId,
            name = stats.Name,
            bucket_seconds = 1,
            points = buckets.Select(b => new {
                t = b.EpochSeconds,
                count = b.Count,
                mean_ns = b.Count == 0 ? 0 : (long)(b.TotalTicks * nsPerTick / b.Count),
                total_ns = (long)(b.TotalTicks * nsPerTick),
            }).ToArray(),
        });
    }

    private static IResult GetCurrentGc(SessionAggregator aggregator, int? limit) {
        int take = QueryLimit.Clamp(limit, DefaultGcEventLimit, MaxGcEventLimit);
        GcEventRecord[] snapshot = aggregator.SnapshotGcEvents(take);
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            total_events = aggregator.TotalGcEvents,
            events = snapshot.Select(e => new {
                generation = e.Generation,
                pause_type = (byte)e.PauseType,
                heap_before = e.HeapBefore,
                heap_after = e.HeapAfter,
                duration_micros = e.DurationMicros,
                ticks = e.Ticks,
                allocation_rate_bpm = e.AllocationRateBytesPerMinute,
                frame_ordinal = e.FrameOrdinal,
            }).ToArray(),
        });
    }

    private static IResult GetCurrentMetrics(SessionAggregator aggregator) {
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            total_observations = aggregator.TotalMetricObservations,
            metrics = aggregator.SnapshotMetrics().Select(m => new {
                id = m.MetricId,
                name = m.Name,
                kind = (byte)m.Kind,
                unit = m.Unit,
                labels = m.Labels.Values.Select(l => new {
                    canonical = l.Canonical,
                    latest_value = Interlocked.Read(ref l.LatestValue),
                    total_sample_count = Interlocked.Read(ref l.TotalSampleCount),
                }).ToArray(),
            }).ToArray(),
        });
    }

    private static IResult GetCurrentPatches(SessionAggregator aggregator) {
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            conflicts_known = aggregator.PatchConflictsKnown,
            conflicts = aggregator.PatchConflicts.Select(c => new {
                section = c.SectionName,
                target_method = c.TargetMethod,
                other_owner = c.OtherOwner,
                patch_type = c.PatchType,
                priority = c.Priority,
                patch_method = c.PatchMethod,
            }).ToArray(),
        });
    }

    private static IResult GetCurrentCallTree(SessionAggregator aggregator, int? depth, int? top) {
        double nsPerTick = NsPerTick(aggregator.Meta);
        int depthCap = depth is int d && d > 0 ? Math.Min(d, MaxCallTreeDepth) : CallTreeBuilder.DefaultDepthCap;
        int topN = top is int t && t > 0 ? Math.Min(t, MaxCallTreeTopN) : CallTreeBuilder.DefaultTopN;

        List<SectionStats> sections = [.. aggregator.SnapshotSections()];
        Dictionary<int, string> names = sections.ToDictionary(s => s.SectionId, s => s.Name);
        Dictionary<int, string?> subsystems = sections.ToDictionary(s => s.SectionId, s => s.Subsystem);
        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(
            aggregator.SnapshotCallEdges(), names, nsPerTick, depthCap, topN, subsystems);

        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            depth_cap = depthCap,
            top_n = topN,
            roots = roots.Select(MapCallNode).ToArray(),
        });
    }

    private static IResult GetSections(SessionAggregator aggregator) {
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            sections = aggregator.SnapshotSections().Select(s => new {
                id = s.SectionId,
                name = s.Name,
                subsystem = s.Subsystem,
            }).ToArray(),
        });
    }

    private static double NsPerTick(SessionMeta? meta) => TickConverter.NsPerTick(meta);

    internal static object MapSession(SessionMeta meta, bool isCurrent, string name = "") {
        return new {
            id = meta.SessionId,
            name = name ?? string.Empty,
            started_utc = new DateTime(meta.StartedUtcTicks, DateTimeKind.Utc),
            library_version = meta.LibraryVersion,
            game_version = meta.GameVersion,
            is_current = isCurrent,
        };
    }

    private static object MapCallNode(CallTreeNode node) {
        return new {
            id = node.SectionId,
            name = node.Name,
            subsystem = node.Subsystem,
            call_count = node.CallCount,
            total_ns = node.TotalNs,
            alloc_bytes = node.AllocBytes,
            is_other = node.IsOther,
            children = node.Children.Select(MapCallNode).ToArray(),
        };
    }
}
