using System;
using System.Diagnostics;
using System.Threading;

namespace RimWorks.RimObs.Observers;

internal sealed class AllocationSampler {
    private static readonly long s_TimestampTicksPerSecond = Stopwatch.Frequency;

    private long _lastHeapBytes;
    private long _windowStartTimestamp;
    private long _windowBytesAccumulator;
    private long _windowSamplesAccumulator;
    private long _totalSamplesEmitted;

    public AllocationSampler() {
        long now = Stopwatch.GetTimestamp();
        _lastHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        _windowStartTimestamp = now;
    }

    public long TotalSamplesEmitted => Interlocked.Read(ref _totalSamplesEmitted);

    public bool TryPollWindow(long windowDurationMs, out AllocationSample sample) {
        long now = Stopwatch.GetTimestamp();
        long heapNow = GC.GetTotalMemory(forceFullCollection: false);

        long heapDelta = heapNow - _lastHeapBytes;
        if (heapDelta > 0) {
            _windowBytesAccumulator += heapDelta;
            _windowSamplesAccumulator++;
        }
        _lastHeapBytes = heapNow;

        long elapsedMs = (now - _windowStartTimestamp) * 1000L / s_TimestampTicksPerSecond;
        if (elapsedMs < windowDurationMs) {
            sample = default;
            return false;
        }

        sample = new AllocationSample(
            windowStartTimestamp: _windowStartTimestamp,
            windowDurationMs: elapsedMs,
            bytesAllocated: _windowBytesAccumulator,
            samplesCount: _windowSamplesAccumulator
        );

        _windowStartTimestamp = now;
        _windowBytesAccumulator = 0;
        _windowSamplesAccumulator = 0;
        Interlocked.Increment(ref _totalSamplesEmitted);
        return true;
    }
}
