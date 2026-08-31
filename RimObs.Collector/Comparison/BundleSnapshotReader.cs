using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RimWorks.RimObs.Collector.Bundle;

namespace RimWorks.RimObs.Collector.Comparison;

public sealed class BundleSnapshotReader {
    private readonly BundleImportRegistry _registry;

    public BundleSnapshotReader(BundleImportRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public SessionSnapshot? Read(string token) {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        if (!_registry.TryGet(token, out BundleImportEntry? entry) || entry is null)
            return null;
        _registry.Touch(token);
        return ReadFromDir(entry.TempDir);
    }

    public static SessionSnapshot? ReadFromDir(string dir) {
        SummaryDto? summary = ReadJson<SummaryDto>(Path.Combine(dir, "session_summary.json"));
        if (summary is null)
            return null;

        HotspotsDto? hotspots = ReadJson<HotspotsDto>(Path.Combine(dir, "hotspots.json"));
        DescriptorsDto? descriptors = ReadJson<DescriptorsDto>(Path.Combine(dir, "metric_descriptors.json"));
        CustomMetricsDto? custom = ReadJson<CustomMetricsDto>(Path.Combine(dir, "custom_metrics.json"));

        List<SectionSnapshot> sections = BuildSections(hotspots);
        Dictionary<int, (long Value, long Samples)> labelTotals = BuildLabelTotals(custom);
        List<MetricSnapshot> metrics = BuildMetrics(descriptors, labelTotals);

        return new SessionSnapshot(
            SessionId: summary.SessionId ?? string.Empty,
            IsCurrent: false,
            LibraryVersion: summary.LibraryVersion ?? string.Empty,
            GameVersion: summary.GameVersion ?? string.Empty,
            StartedUtcTicks: ToUtcTicks(summary.StartedUtc),
            Sections: sections,
            Metrics: metrics);
    }

    private static List<SectionSnapshot> BuildSections(HotspotsDto? hotspots) {
        List<SectionSnapshot> sections = [];
        if (hotspots?.Hotspots is not null) {
            foreach (HotspotDto h in hotspots.Hotspots) {
                sections.Add(new SectionSnapshot(
                    SectionId: h.Id,
                    Name: h.Name,
                    Owner: OwnerName.FromSection(h.Name),
                    SampleCount: h.SampleCount,
                    TotalNs: h.TotalNs,
                    MinNs: 0,
                    MaxNs: 0));
            }
        }
        return sections;
    }

    private static Dictionary<int, (long Value, long Samples)> BuildLabelTotals(CustomMetricsDto? custom) {
        Dictionary<int, (long Value, long Samples)> labelTotals = [];
        if (custom?.Metrics is not null) {
            foreach (CustomMetricDto m in custom.Metrics) {
                long value = 0;
                long samples = 0;
                if (m.Labels is not null) {
                    foreach (LabelDto l in m.Labels) {
                        value += l.LatestValue;
                        samples += l.TotalSampleCount;
                    }
                }
                labelTotals[m.Id] = (value, samples);
            }
        }
        return labelTotals;
    }

    private static List<MetricSnapshot> BuildMetrics(DescriptorsDto? descriptors, Dictionary<int, (long Value, long Samples)> labelTotals) {
        List<MetricSnapshot> metrics = [];
        if (descriptors?.Metrics is not null) {
            foreach (DescriptorDto d in descriptors.Metrics) {
                labelTotals.TryGetValue(d.Id, out (long Value, long Samples) totals);
                metrics.Add(new MetricSnapshot(
                    MetricId: d.Id,
                    Name: d.Name,
                    Owner: OwnerName.FromSection(d.Name),
                    Kind: d.Kind,
                    Unit: d.Unit ?? string.Empty,
                    Value: totals.Value,
                    TotalSampleCount: totals.Samples));
            }
        }
        return metrics;
    }

    private static long ToUtcTicks(DateTime dt) {
        return dt.Kind switch {
            DateTimeKind.Utc => dt.Ticks,
            DateTimeKind.Local => dt.ToUniversalTime().Ticks,
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc).Ticks,
        };
    }

    private static T? ReadJson<T>(string path) where T : class {
        if (!File.Exists(path))
            return null;
        try {
            using FileStream fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(fs, BundleManifest.JsonOptions);
        }
        catch (JsonException) {
            return null;
        }
    }

    private sealed record SummaryDto(string? SessionId, DateTime StartedUtc, string? LibraryVersion, string? GameVersion);
    private sealed record HotspotsDto(HotspotDto[]? Hotspots);
    private sealed record HotspotDto(int Id, string Name, long SampleCount, long TotalNs);
    private sealed record DescriptorsDto(DescriptorDto[]? Metrics);
    private sealed record DescriptorDto(int Id, string Name, byte Kind, string? Unit);
    private sealed record CustomMetricsDto(CustomMetricDto[]? Metrics);
    private sealed record CustomMetricDto(int Id, string Name, LabelDto[]? Labels);
    private sealed record LabelDto(string Canonical, long LatestValue, long TotalSampleCount);
}
