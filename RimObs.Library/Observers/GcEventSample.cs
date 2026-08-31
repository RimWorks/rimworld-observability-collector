using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Observers;

internal readonly struct GcEventSample {
    public GcEventSample(byte generation, GcPauseType pauseType, long heapBefore, long heapAfter, long durationMicros, long tick, long allocationRateBytesPerMinute) {
        Generation = generation;
        PauseType = pauseType;
        HeapBefore = heapBefore;
        HeapAfter = heapAfter;
        DurationMicros = durationMicros;
        Tick = tick;
        AllocationRateBytesPerMinute = allocationRateBytesPerMinute;
    }

    public byte Generation { get; }
    public GcPauseType PauseType { get; }
    public long HeapBefore { get; }
    public long HeapAfter { get; }
    public long DurationMicros { get; }
    public long Tick { get; }
    public long AllocationRateBytesPerMinute { get; }
}
