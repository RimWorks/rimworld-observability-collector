namespace RimWorks.RimObs.Wire;

public sealed class AllocationsBatch {
    public long[] WindowStartTimestamps { get; set; } = [];

    public long[] WindowDurationsMs { get; set; } = [];

    public long[] BytesAllocated { get; set; } = [];

    public long[] SamplesCount { get; set; } = [];
}
