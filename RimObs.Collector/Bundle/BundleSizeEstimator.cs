using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Bundle;

public sealed class BundleEstimateInput {
    public int SectionCount { get; set; }
    public int MetricCount { get; set; }
    public int AllocationRowCount { get; set; }
    public int CallEdgeCount { get; set; }
    public int GcEventCount { get; set; }
    public int PatchConflictCount { get; set; }
    public long MetricsSqliteBytes { get; set; }
    public IReadOnlySet<BundleContentKey> Includes { get; set; } = new HashSet<BundleContentKey>();
}

public readonly record struct BundleSizeEstimate(long TotalBytes) {
    public bool ExceedsSoftCap => TotalBytes > BundleSizeEstimator.SoftCapBytes;
}

public static class BundleSizeEstimator {
    public const long SoftCapBytes = 25L * 1024 * 1024;

    // Mandatory fixed overhead present in every bundle
    private const int ManifestBytes = 1 * 1024;
    private const int SessionSummaryBytes = 4 * 1024;
    private const int CollectorHealthBytes = 4 * 1024;

    // Per-item calibration constants (~200 bytes/allocation row, ~150 bytes/call edge, etc.)
    private const int HotspotBytesPerSection = 256;
    private const int DescriptorBytesPerMetric = 256;
    private const int CustomMetricBytesPerMetric = 192;
    private const int AllocationBytesPerRow = 200;
    private const int CallEdgeBytesPerRow = 150;
    private const int GcEventBytesPerRow = 120;
    private const int PatchBytesPerRow = 256;

    public static BundleSizeEstimate Estimate(BundleEstimateInput input) {
        long bytes = ManifestBytes + SessionSummaryBytes + CollectorHealthBytes;
        bytes += input.SectionCount * (long)HotspotBytesPerSection;
        bytes += input.MetricCount * (long)(DescriptorBytesPerMetric + CustomMetricBytesPerMetric);

        if (input.Includes.Contains(BundleContentKey.Allocations))
            bytes += input.AllocationRowCount * (long)AllocationBytesPerRow;
        if (input.Includes.Contains(BundleContentKey.CallHierarchy))
            bytes += input.CallEdgeCount * (long)CallEdgeBytesPerRow;
        if (input.Includes.Contains(BundleContentKey.GcEvents))
            bytes += input.GcEventCount * (long)GcEventBytesPerRow;
        if (input.Includes.Contains(BundleContentKey.Patches))
            bytes += input.PatchConflictCount * (long)PatchBytesPerRow;
        if (input.Includes.Contains(BundleContentKey.MetricsSqlite))
            bytes += input.MetricsSqliteBytes;

        return new BundleSizeEstimate(bytes);
    }
}
