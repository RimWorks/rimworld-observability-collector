using System;
using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Bundle;
using RimWorks.RimObs.Collector.Comparison;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace RimWorks.RimObs.Collector.Api;

public static class ComparisonEndpoints {
    private const int DefaultHotspotLimit = 50;
    private const int MaxHotspotLimit = 500;
    private const string UnknownSessionReason = "unknown session";

    public static IEndpointRouteBuilder MapComparisonEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/sessions/current/load_order", GetCurrentLoadOrder);
        endpoints.MapGet("/api/v1/sessions/{id}/summary", GetSessionSummary);
        endpoints.MapGet("/api/v1/sessions/{id}/hotspots", GetSessionHotspots);
        endpoints.MapGet("/api/v1/sessions/{id}/metrics", GetSessionMetrics);
        endpoints.MapGet("/api/v1/sessions/{id}/load_order", GetSessionLoadOrder);
        endpoints.MapGet("/api/v1/sessions/compare", CompareSessions);
        return endpoints;
    }

    private static IResult GetCurrentLoadOrder(SessionAggregator aggregator, IServiceProvider services) {
        SessionSnapshot? snapshot = Reader(aggregator, services).ReadCurrent();
        if (snapshot is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no active session" });
        return Results.Ok(LoadOrderPayload(snapshot));
    }

    private static IResult GetSessionSummary(string id, SessionAggregator aggregator, IServiceProvider services) {
        SessionSnapshot? snapshot = Reader(aggregator, services).Read(id);
        if (snapshot is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = UnknownSessionReason });

        long totalNs = 0;
        long totalSamples = 0;
        foreach (SectionSnapshot s in snapshot.Sections) {
            totalNs += s.TotalNs;
            totalSamples += s.SampleCount;
        }

        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            session = new {
                id = snapshot.SessionId,
                started_utc = new DateTime(snapshot.StartedUtcTicks, DateTimeKind.Utc),
                library_version = snapshot.LibraryVersion,
                game_version = snapshot.GameVersion,
                is_current = snapshot.IsCurrent,
            },
            section_count = snapshot.Sections.Count,
            metric_count = snapshot.Metrics.Count,
            total_samples = totalSamples,
            total_section_ns = totalNs,
        });
    }

    private static IResult GetSessionHotspots(string id, int? limit, SessionAggregator aggregator, IServiceProvider services) {
        SessionSnapshot? snapshot = Reader(aggregator, services).Read(id);
        if (snapshot is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = UnknownSessionReason });

        int take = QueryLimit.Clamp(limit, DefaultHotspotLimit, MaxHotspotLimit);
        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            unit = "ns",
            hotspots = snapshot.Sections
                .OrderByDescending(s => s.TotalNs)
                .Take(take)
                .Select(s => new {
                    id = s.SectionId,
                    name = s.Name,
                    owner = s.Owner,
                    sample_count = s.SampleCount,
                    total_ns = s.TotalNs,
                    mean_ns = s.MeanNs,
                    min_ns = s.MinNs,
                    max_ns = s.MaxNs,
                })
                .ToArray(),
        });
    }

    private static IResult GetSessionMetrics(string id, SessionAggregator aggregator, IServiceProvider services) {
        SessionSnapshot? snapshot = Reader(aggregator, services).Read(id);
        if (snapshot is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = UnknownSessionReason });

        return Results.Ok(new {
            schema_version = SchemaVersion.Current,
            metrics = snapshot.Metrics.Select(m => new {
                id = m.MetricId,
                name = m.Name,
                owner = m.Owner,
                kind = m.Kind,
                unit = m.Unit,
                value = m.Value,
                total_sample_count = m.TotalSampleCount,
            }).ToArray(),
        });
    }

    private static IResult GetSessionLoadOrder(string id, SessionAggregator aggregator, IServiceProvider services) {
        SessionSnapshot? snapshot = Reader(aggregator, services).Read(id);
        if (snapshot is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = UnknownSessionReason });
        return Results.Ok(LoadOrderPayload(snapshot));
    }

    private static IResult CompareSessions(string? @base, string? head, SessionAggregator aggregator, IServiceProvider services) {
        if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head))
            return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "base and head query parameters are required" });

        SessionSnapshotReader reader = Reader(aggregator, services);
        BundleSnapshotReader? bundleReader = BundleReader(services);
        SessionSnapshot? baseline = ResolveSource(@base, reader, bundleReader);
        if (baseline is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "unknown base source" });
        SessionSnapshot? headSnapshot = ResolveSource(head, reader, bundleReader);
        if (headSnapshot is null)
            return Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "unknown head source" });

        ComparisonResult result = SessionComparer.Compare(baseline, headSnapshot);
        return Results.Ok(MapComparison(result));
    }

    private const string BundleSourcePrefix = "bundle:";

    private static SessionSnapshotReader Reader(SessionAggregator aggregator, IServiceProvider services) {
        return new SessionSnapshotReader(aggregator, services.GetService<SqliteSessionPersister>());
    }

    private static BundleSnapshotReader? BundleReader(IServiceProvider services) {
        BundleImportRegistry? registry = services.GetService<BundleImportRegistry>();
        return registry is null ? null : new BundleSnapshotReader(registry);
    }

    private static SessionSnapshot? ResolveSource(string source, SessionSnapshotReader sessionReader, BundleSnapshotReader? bundleReader) {
        if (source.StartsWith(BundleSourcePrefix, StringComparison.Ordinal))
            return bundleReader?.Read(source.Substring(BundleSourcePrefix.Length));
        return sessionReader.Read(source);
    }

    private static object LoadOrderPayload(SessionSnapshot snapshot) {
        return new {
            schema_version = SchemaVersion.Current,
            unit = "ns",
            note = "Owners are derived from instrumented section name prefixes, not the game's authoritative mod load order.",
            session_id = snapshot.SessionId,
            owners = snapshot.Sections
                .GroupBy(s => s.Owner, StringComparer.Ordinal)
                .Select(g => new {
                    owner = g.Key,
                    section_count = g.Count(),
                    total_ns = g.Sum(s => s.TotalNs),
                })
                .OrderByDescending(o => o.total_ns)
                .ToArray(),
        };
    }

    private static object MapComparison(ComparisonResult result) {
        return new {
            schema_version = SchemaVersion.Current,
            unit = "ns",
            disclaimer = "Deltas indicate correlation, not causation. Flagged items are likely regression candidates, not confirmed causes.",
            @base = MapRef(result.Base),
            head = MapRef(result.Head),
            timing = new {
                base_total_ns = result.Timing.BaseTotalNs,
                head_total_ns = result.Timing.HeadTotalNs,
                delta_ns = result.Timing.DeltaNs,
                delta_percent = result.Timing.DeltaPercent,
                base_sample_count = result.Timing.BaseSampleCount,
                head_sample_count = result.Timing.HeadSampleCount,
                base_mean_ns = result.Timing.BaseMeanNs,
                head_mean_ns = result.Timing.HeadMeanNs,
                delta_mean_ns = result.Timing.DeltaMeanNs,
            },
            hotspots = result.Hotspots.Select(h => new {
                id = h.SectionId,
                name = h.Name,
                owner = h.Owner,
                status = h.Status.ToWireString(),
                base_total_ns = h.BaseTotalNs,
                head_total_ns = h.HeadTotalNs,
                delta_ns = h.DeltaNs,
                delta_percent = h.DeltaPercent,
                base_mean_ns = h.BaseMeanNs,
                head_mean_ns = h.HeadMeanNs,
                likely_regression_candidate = h.LikelyRegressionCandidate,
            }).ToArray(),
            mod_costs = result.ModCosts.Select(m => new {
                owner = m.Owner,
                status = m.Status.ToWireString(),
                base_total_ns = m.BaseTotalNs,
                head_total_ns = m.HeadTotalNs,
                delta_ns = m.DeltaNs,
                delta_percent = m.DeltaPercent,
                likely_regression_candidate = m.LikelyRegressionCandidate,
            }).ToArray(),
            metrics = result.Metrics.Select(m => new {
                name = m.Name,
                owner = m.Owner,
                kind = m.Kind,
                unit = m.Unit,
                status = m.Status.ToWireString(),
                base_value = m.BaseValue,
                head_value = m.HeadValue,
                delta_value = m.DeltaValue,
                delta_percent = m.DeltaPercent,
            }).ToArray(),
            load_order = new {
                added = result.LoadOrder.Added,
                removed = result.LoadOrder.Removed,
                common = result.LoadOrder.Common,
            },
            warnings = result.Warnings,
        };
    }

    private static object MapRef(SessionRef r) {
        return new {
            id = r.Id,
            library_version = r.LibraryVersion,
            game_version = r.GameVersion,
            started_utc = new DateTime(r.StartedUtcTicks, DateTimeKind.Utc),
        };
    }
}
