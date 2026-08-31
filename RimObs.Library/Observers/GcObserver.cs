using System;
using System.Diagnostics;
using System.Threading;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Observers;

internal sealed class GcObserver {
    private static readonly long s_TimestampTicksPerSecond = Stopwatch.Frequency;
    private const long MicrosPerSecond = 1_000_000L;

    private readonly int[] _lastCounts;
    private readonly int _maxGeneration;

    private long _lastHeapBytes;
    private long _lastHeapTimestamp;
    private long _allocationRateBytesPerMinute;
    private long _eventsObserved;

    public GcObserver() {
        _maxGeneration = GC.MaxGeneration;
        _lastCounts = new int[_maxGeneration + 1];
        for (int gen = 0; gen <= _maxGeneration; gen++)
            _lastCounts[gen] = GC.CollectionCount(gen);
        _lastHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        _lastHeapTimestamp = Stopwatch.GetTimestamp();
    }

    public int MaxGeneration => _maxGeneration;

    public long EventsObserved => Interlocked.Read(ref _eventsObserved);

    public long AllocationRateBytesPerMinute => Interlocked.Read(ref _allocationRateBytesPerMinute);

    public bool TryPoll(long currentTick, out GcEventSample sample) {
        long heapNow = GC.GetTotalMemory(forceFullCollection: false);
        long timestampNow = Stopwatch.GetTimestamp();

        long heapDelta = heapNow - _lastHeapBytes;
        long elapsedTimestampTicks = timestampNow - _lastHeapTimestamp;
        if (heapDelta > 0 && elapsedTimestampTicks > 0) {
            long bytesPerSecond = heapDelta * s_TimestampTicksPerSecond / elapsedTimestampTicks;
            Interlocked.Exchange(ref _allocationRateBytesPerMinute, bytesPerSecond * 60);
        }

        int detectedGeneration = -1;
        for (int gen = _maxGeneration; gen >= 0; gen--) {
            int current = GC.CollectionCount(gen);
            if (current != _lastCounts[gen]) {
                if (detectedGeneration == -1)
                    detectedGeneration = gen;
                _lastCounts[gen] = current;
            }
        }

        _lastHeapTimestamp = timestampNow;

        if (detectedGeneration < 0) {
            _lastHeapBytes = heapNow;
            sample = default;
            return false;
        }

        long heapBefore = _lastHeapBytes;
        long heapAfter = heapNow;
        _lastHeapBytes = heapNow;

        long durationMicros = elapsedTimestampTicks > 0
            ? elapsedTimestampTicks * MicrosPerSecond / s_TimestampTicksPerSecond
            : 0;

        Interlocked.Increment(ref _eventsObserved);

        sample = new GcEventSample(
            generation: (byte)detectedGeneration,
            pauseType: GcPauseType.Foreground,
            heapBefore: heapBefore,
            heapAfter: heapAfter,
            durationMicros: durationMicros,
            tick: currentTick,
            allocationRateBytesPerMinute: Interlocked.Read(ref _allocationRateBytesPerMinute)
        );
        return true;
    }
}
