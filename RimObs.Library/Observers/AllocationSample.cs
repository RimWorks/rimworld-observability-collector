namespace RimWorks.RimObs.Observers;

internal readonly struct AllocationSample {
    public AllocationSample(long windowStartTimestamp, long windowDurationMs, long bytesAllocated, long samplesCount) {
        WindowStartTimestamp = windowStartTimestamp;
        WindowDurationMs = windowDurationMs;
        BytesAllocated = bytesAllocated;
        SamplesCount = samplesCount;
    }

    public long WindowStartTimestamp { get; }
    public long WindowDurationMs { get; }
    public long BytesAllocated { get; }
    public long SamplesCount { get; }
}
