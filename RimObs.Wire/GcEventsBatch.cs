namespace RimWorks.RimObs.Wire;

public sealed class GcEventsBatch {
    public byte[] Generations { get; set; } = [];

    public byte[] PauseTypes { get; set; } = [];

    public long[] HeapBefore { get; set; } = [];

    public long[] HeapAfter { get; set; } = [];

    public long[] DurationMicros { get; set; } = [];

    public long[] Ticks { get; set; } = [];

    public long[] AllocationRateBytesPerMinute { get; set; } = [];
}
