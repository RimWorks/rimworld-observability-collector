using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Aggregation;

public readonly struct GcEventRecord {
    public GcEventRecord(
        byte generation,
        GcPauseType pauseType,
        long heapBefore,
        long heapAfter,
        long durationMicros,
        long ticks,
        long allocationRateBytesPerMinute
    ) {
        Generation = generation;
        PauseType = pauseType;
        HeapBefore = heapBefore;
        HeapAfter = heapAfter;
        DurationMicros = durationMicros;
        Ticks = ticks;
        AllocationRateBytesPerMinute = allocationRateBytesPerMinute;
    }

    public byte Generation { get; }
    public GcPauseType PauseType { get; }
    public long HeapBefore { get; }
    public long HeapAfter { get; }
    public long DurationMicros { get; }
    public long Ticks { get; }
    public long AllocationRateBytesPerMinute { get; }
}
