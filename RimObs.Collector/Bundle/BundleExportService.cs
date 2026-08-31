using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Bundle;

public enum BundleExportStatus {
    Ok,
    UnknownSession,
    ExceedsSoftCap,
    NoActiveSession,
}

public sealed class BundleExportRequest {
    public string SessionId { get; set; } = string.Empty;
    public IReadOnlySet<BundleContentKey> Includes { get; set; } = new HashSet<BundleContentKey>();
    public bool Force { get; set; }
}

public sealed class BundleExportResult {
    public BundleExportStatus Status { get; init; }
    public byte[]? Bytes { get; init; }
    public long EstimatedBytes { get; init; }
}

public sealed class BundleEstimateResult {
    public BundleExportStatus Status { get; init; }
    public long EstimatedBytes { get; init; }
    public bool ExceedsSoftCap { get; init; }
}

public sealed class BundleExportService {
    public const int BundleSchemaVersion = 1;

    private readonly SessionAggregator _aggregator;
    private readonly string _collectorVersion;
    private readonly DateTimeOffset _startedUtc;

    internal Func<BundleEstimateInput, BundleSizeEstimate>? EstimateOverride { get; set; }

    public BundleExportService(SessionAggregator aggregator, string collectorVersion, DateTimeOffset? startedUtc = null) {
        _aggregator = aggregator;
        _collectorVersion = collectorVersion;
        _startedUtc = startedUtc ?? ProcessStartUtc();
    }

    private static DateTimeOffset ProcessStartUtc() {
        using Process process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }

    public Task<BundleExportResult> ExportAsync(BundleExportRequest request, CancellationToken cancellationToken) {
        SessionMeta? meta = _aggregator.Meta;
        if (meta is null)
            return Task.FromResult(new BundleExportResult { Status = BundleExportStatus.NoActiveSession });
        if (!string.Equals(meta.SessionId, request.SessionId, StringComparison.Ordinal))
            return Task.FromResult(new BundleExportResult { Status = BundleExportStatus.UnknownSession });

        BundleEstimateInput estimateInput = BuildEstimateInput(request.Includes);
        BundleSizeEstimate estimate = EstimateOverride is not null
            ? EstimateOverride(estimateInput)
            : BundleSizeEstimator.Estimate(estimateInput);

        if (estimate.ExceedsSoftCap && !request.Force) {
            return Task.FromResult(new BundleExportResult {
                Status = BundleExportStatus.ExceedsSoftCap,
                EstimatedBytes = estimate.TotalBytes,
            });
        }

        byte[] bytes = BuildZip(meta, request.Includes);
        return Task.FromResult(new BundleExportResult {
            Status = BundleExportStatus.Ok,
            Bytes = bytes,
            EstimatedBytes = estimate.TotalBytes,
        });
    }

    public BundleEstimateResult Estimate(string sessionId, IReadOnlySet<BundleContentKey> includes) {
        SessionMeta? meta = _aggregator.Meta;
        if (meta is null)
            return new BundleEstimateResult { Status = BundleExportStatus.NoActiveSession };
        if (!string.Equals(meta.SessionId, sessionId, StringComparison.Ordinal))
            return new BundleEstimateResult { Status = BundleExportStatus.UnknownSession };

        BundleEstimateInput estimateInput = BuildEstimateInput(includes);
        BundleSizeEstimate estimate = EstimateOverride is not null
            ? EstimateOverride(estimateInput)
            : BundleSizeEstimator.Estimate(estimateInput);

        return new BundleEstimateResult {
            Status = BundleExportStatus.Ok,
            EstimatedBytes = estimate.TotalBytes,
            ExceedsSoftCap = estimate.ExceedsSoftCap,
        };
    }

    private BundleEstimateInput BuildEstimateInput(IReadOnlySet<BundleContentKey> includes) {
        return new BundleEstimateInput {
            SectionCount = _aggregator.SectionCount,
            MetricCount = _aggregator.MetricCount,
            AllocationRowCount = 0,
            CallEdgeCount = _aggregator.CallEdges.Count,
            GcEventCount = (int)_aggregator.TotalGcEvents,
            PatchConflictCount = _aggregator.PatchConflicts.Count,
            MetricsSqliteBytes = 0,
            Includes = includes,
        };
    }

    private byte[] BuildZip(SessionMeta meta, IReadOnlySet<BundleContentKey> includes) {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true)) {
            List<string> entryNames = new List<string>();

            WriteJson(zip, "session_summary.json", BuildSessionSummary(meta));
            entryNames.Add("session_summary.json");

            WriteJson(zip, "metric_descriptors.json", BuildMetricDescriptors());
            entryNames.Add("metric_descriptors.json");

            WriteJson(zip, "hotspots.json", BuildHotspots(meta));
            entryNames.Add("hotspots.json");

            WriteJson(zip, "custom_metrics.json", BuildCustomMetrics());
            entryNames.Add("custom_metrics.json");

            WriteJson(zip, "load_order.json", BuildLoadOrder());
            entryNames.Add("load_order.json");

            WriteJson(zip, "collector_health.json", BuildCollectorHealth());
            entryNames.Add("collector_health.json");

            if (includes.Contains(BundleContentKey.Allocations)) {
                WriteJson(zip, "allocations.json", BuildAllocations());
                entryNames.Add("allocations.json");
            }
            if (includes.Contains(BundleContentKey.GcEvents)) {
                WriteJson(zip, "gc_events.json", BuildGcEvents());
                entryNames.Add("gc_events.json");
            }
            if (includes.Contains(BundleContentKey.Patches)) {
                WriteJson(zip, "patches.json", BuildPatches());
                entryNames.Add("patches.json");
            }
            if (includes.Contains(BundleContentKey.CallHierarchy)) {
                WriteJson(zip, "call_hierarchy.json", BuildCallHierarchy(meta));
                entryNames.Add("call_hierarchy.json");
            }

            string reportHtml = BuildReportHtml(meta, entryNames);
            WriteText(zip, "report.html", reportHtml);
            entryNames.Add("report.html");

            BundleManifest manifest = new BundleManifest {
                SchemaVersion = BundleSchemaVersion,
                SessionId = meta.SessionId,
                CreatedUtc = DateTime.UtcNow,
                CollectorVersion = _collectorVersion,
                Entries = entryNames.ToArray(),
            };
            WriteJson(zip, "manifest.json", manifest);
        }
        return output.ToArray();
    }

    private static void WriteJson(ZipArchive zip, string name, object payload) {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        JsonSerializer.Serialize(writer, payload, BundleManifest.JsonOptions);
    }

    private static void WriteText(ZipArchive zip, string name, string content) {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private object BuildSessionSummary(SessionMeta meta) {
        return new {
            session_id = meta.SessionId,
            started_utc = new DateTime(meta.StartedUtcTicks, DateTimeKind.Utc),
            library_version = meta.LibraryVersion,
            game_version = meta.GameVersion,
            section_count = _aggregator.SectionCount,
            metric_count = _aggregator.MetricCount,
            total_batches = _aggregator.TotalBatches,
            total_samples = _aggregator.TotalSamples,
            total_bytes = _aggregator.TotalBytes,
            total_gc_events = _aggregator.TotalGcEvents,
            total_allocations = _aggregator.TotalAllocations,
            total_metric_observations = _aggregator.TotalMetricObservations,
        };
    }

    private object BuildMetricDescriptors() {
        return new {
            metrics = _aggregator.Metrics.Select(m => new {
                id = m.MetricId,
                name = m.Name,
                kind = (byte)m.Kind,
                unit = m.Unit,
            }).ToArray(),
        };
    }

    private object BuildHotspots(SessionMeta meta) {
        double nsPerTick = TickConverter.NsPerTick(meta);
        return new {
            hotspots = _aggregator.Sections
                .OrderByDescending(s => s.TotalElapsedTicks)
                .Select(s => new {
                    id = s.SectionId,
                    name = s.Name,
                    sample_count = s.SampleCount,
                    total_ns = (long)(s.TotalElapsedTicks * nsPerTick),
                })
                .ToArray(),
        };
    }

    private object BuildCustomMetrics() {
        return new {
            metrics = _aggregator.Metrics.Select(m => new {
                id = m.MetricId,
                name = m.Name,
                labels = m.Labels.Values.Select(l => new {
                    canonical = l.Canonical,
                    latest_value = Interlocked.Read(ref l.LatestValue),
                    total_sample_count = Interlocked.Read(ref l.TotalSampleCount),
                }).ToArray(),
            }).ToArray(),
        };
    }

    private static object BuildLoadOrder() {
        return new { mods = Array.Empty<object>() };
    }

    private object BuildCollectorHealth() {
        return new {
            uptime_seconds = Math.Max(0, (DateTimeOffset.UtcNow - _startedUtc).TotalSeconds),
            total_batches = _aggregator.TotalBatches,
            total_bytes = _aggregator.TotalBytes,
        };
    }

    private static object BuildAllocations() {
        return new { allocations = Array.Empty<object>() };
    }

    private object BuildGcEvents() {
        GcEventRecord[] events = _aggregator.SnapshotGcEvents(1024);
        return new {
            total_events = _aggregator.TotalGcEvents,
            events = events.Select(e => new {
                generation = e.Generation,
                pause_type = (byte)e.PauseType,
                heap_before = e.HeapBefore,
                heap_after = e.HeapAfter,
                duration_micros = e.DurationMicros,
                ticks = e.Ticks,
            }).ToArray(),
        };
    }

    private object BuildPatches() {
        return new {
            conflicts = _aggregator.PatchConflicts.Select(c => new {
                section = c.SectionName,
                target_method = c.TargetMethod,
                other_owner = c.OtherOwner,
                patch_type = c.PatchType,
                priority = c.Priority,
                patch_method = c.PatchMethod,
            }).ToArray(),
        };
    }

    private object BuildCallHierarchy(SessionMeta meta) {
        double nsPerTick = TickConverter.NsPerTick(meta);
        Dictionary<int, string> names = _aggregator.Sections.ToDictionary(s => s.SectionId, s => s.Name);
        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(_aggregator.CallEdges, names, nsPerTick, CallTreeBuilder.DefaultDepthCap, CallTreeBuilder.DefaultTopN);
        return new { roots = roots };
    }

    private string BuildReportHtml(SessionMeta meta, IReadOnlyList<string> entryNames) {
        string template = ReportHtmlBuilder.LoadTemplate();
        object payload = new {
            manifest = new {
                schema_version = BundleSchemaVersion,
                session_id = meta.SessionId,
                created_utc = DateTime.UtcNow,
                collector_version = _collectorVersion,
                entries = entryNames,
            },
            session_summary = BuildSessionSummary(meta),
            metric_descriptors = BuildMetricDescriptors(),
            hotspots = BuildHotspots(meta),
            custom_metrics = BuildCustomMetrics(),
            load_order = BuildLoadOrder(),
            collector_health = BuildCollectorHealth(),
        };
        string json = JsonSerializer.Serialize(payload, BundleManifest.JsonOptions);
        return ReportHtmlBuilder.InjectBundle(template, json);
    }
}
