using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Comparison;

public sealed record ComparisonResult(
    SessionRef Base,
    SessionRef Head,
    TimingDelta Timing,
    IReadOnlyList<SectionDelta> Hotspots,
    IReadOnlyList<OwnerDelta> ModCosts,
    IReadOnlyList<MetricDelta> Metrics,
    LoadOrderDiff LoadOrder,
    IReadOnlyList<string> Warnings);

public sealed record SessionRef(string Id, string LibraryVersion, string GameVersion, long StartedUtcTicks);

public sealed record TimingDelta(
    long BaseTotalNs,
    long HeadTotalNs,
    long DeltaNs,
    double? DeltaPercent,
    long BaseSampleCount,
    long HeadSampleCount,
    long BaseMeanNs,
    long HeadMeanNs,
    long DeltaMeanNs);

public sealed record SectionDelta(
    int SectionId,
    string Name,
    string Owner,
    DeltaStatus Status,
    long BaseTotalNs,
    long HeadTotalNs,
    long DeltaNs,
    double? DeltaPercent,
    long BaseMeanNs,
    long HeadMeanNs,
    bool LikelyRegressionCandidate);

public sealed record OwnerDelta(
    string Owner,
    DeltaStatus Status,
    long BaseTotalNs,
    long HeadTotalNs,
    long DeltaNs,
    double? DeltaPercent,
    bool LikelyRegressionCandidate);

public sealed record MetricDelta(
    string Name,
    string Owner,
    byte Kind,
    string Unit,
    DeltaStatus Status,
    long BaseValue,
    long HeadValue,
    long DeltaValue,
    double? DeltaPercent);

public sealed record LoadOrderDiff(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Common);
