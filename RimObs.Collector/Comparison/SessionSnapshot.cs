using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Comparison;

public sealed record SessionSnapshot(
    string SessionId,
    bool IsCurrent,
    string LibraryVersion,
    string GameVersion,
    long StartedUtcTicks,
    IReadOnlyList<SectionSnapshot> Sections,
    IReadOnlyList<MetricSnapshot> Metrics);

public sealed record SectionSnapshot(
    int SectionId,
    string Name,
    string Owner,
    long SampleCount,
    long TotalNs,
    long MinNs,
    long MaxNs,
    string? Subsystem = null) {
    public long MeanNs => SampleCount == 0 ? 0 : TotalNs / SampleCount;
}

public sealed record MetricSnapshot(
    int MetricId,
    string Name,
    string Owner,
    byte Kind,
    string Unit,
    long Value,
    long TotalSampleCount);
