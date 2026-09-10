using System;
using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Aggregation;

public sealed record FrameSnapshot(
    int CaptureOrdinal,
    long StartTicks,
    long EndTicks,
    int[] SectionIds,
    int[] ParentIds,
    int[] NodeIds,
    int[] ParentNodeIds,
    long[] NodeStartTicks,
    long[] NodeElapsedTicks,
    long[] NodeAllocBytes,
    int[] ThreadIds) {
    public int NodeCount => SectionIds.Length;

    public long DurationTicks => EndTicks - StartTicks;
}

public sealed record FrameRingStats(
    int FrameCount,
    long MedianDurationTicks,
    long P75DurationTicks,
    long P90DurationTicks,
    long P99DurationTicks,
    long MinDurationTicks,
    long MaxDurationTicks,
    int NewestOrdinal,
    int OldestOrdinal);

/// <summary>
/// Frames cut out of the sample stream by their ordinal, newest last. Samples arrive in
/// ordinal order because the library ring drains in write order, so one open frame is enough.
/// </summary>
public sealed class FrameRing {
    public const int DefaultCapacity = 2000;

    private FrameSnapshot[] _buffer;
    private readonly object _gate = new();
    private readonly List<int> _openSectionIds = [];
    private readonly List<int> _openParentIds = [];
    private readonly List<int> _openNodeIds = [];
    private readonly List<int> _openParentNodeIds = [];
    private readonly List<long> _openStartTicks = [];
    private readonly List<long> _openElapsedTicks = [];
    private readonly List<long> _openAllocBytes = [];
    private readonly List<int> _openThreadIds = [];
    private int _next;
    private int _count;
    private int _openOrdinal = -1;
    private long _preFrameSamples;
    private long _lateSamples;
    private bool _openHasThreadIds;

    public FrameRing()
        : this(DefaultCapacity) {
    }

    public FrameRing(int capacity) {
        _buffer = new FrameSnapshot[Math.Max(1, capacity)];
    }

    public int Capacity => _buffer.Length;

    public int Count {
        get {
            lock (_gate) {
                return _count;
            }
        }
    }

    public long PreFrameSamples {
        get {
            lock (_gate) {
                return _preFrameSamples;
            }
        }
    }

    public long LateSamples {
        get {
            lock (_gate) {
                return _lateSamples;
            }
        }
    }

    public void Add(int frameOrdinal, int sectionId, int parentId, int nodeId, int parentNodeId, long startTicks, long elapsedTicks, long allocBytes = 0L, int threadId = 0) {
        lock (_gate) {
            if (frameOrdinal <= 0) {
                _preFrameSamples++;
                return;
            }
            if (frameOrdinal < _openOrdinal) {
                _lateSamples++;
                return;
            }
            if (frameOrdinal != _openOrdinal) {
                SealOpen();
                _openOrdinal = frameOrdinal;
            }
            _openSectionIds.Add(sectionId);
            _openParentIds.Add(parentId);
            _openNodeIds.Add(nodeId);
            _openParentNodeIds.Add(parentNodeId);
            _openStartTicks.Add(startTicks);
            _openElapsedTicks.Add(elapsedTicks);
            _openAllocBytes.Add(allocBytes);
            _openThreadIds.Add(threadId);
            if (threadId != 0)
                _openHasThreadIds = true;
        }
    }

    public FrameSnapshot? Latest() {
        lock (_gate) {
            if (_count == 0)
                return null;
            return _buffer[(_next - 1 + _buffer.Length) % _buffer.Length];
        }
    }

    public FrameSnapshot[] Snapshot() {
        lock (_gate) {
            if (_count == 0)
                return [];
            FrameSnapshot[] frames = new FrameSnapshot[_count];
            int start = _count < _buffer.Length ? 0 : _next;
            for (int i = 0; i < _count; i++)
                frames[i] = _buffer[(start + i) % _buffer.Length];
            return frames;
        }
    }

    /// <summary>
    /// Swaps in a buffer of a new size, keeping the newest frames that still fit. Shrinking
    /// drops the oldest; the open frame and the drop counters survive either way.
    /// </summary>
    public void Resize(int capacity) {
        int size = Math.Max(1, capacity);
        lock (_gate) {
            if (size == _buffer.Length)
                return;
            int take = Math.Min(_count, size);
            FrameSnapshot[] next = new FrameSnapshot[size];
            int start = _count < _buffer.Length ? 0 : _next;
            for (int i = 0; i < take; i++)
                next[i] = _buffer[(start + _count - take + i) % _buffer.Length];
            _buffer = next;
            _count = take;
            _next = take % size;
        }
    }

    /// <summary>One (ordinal, duration) pair per frame, newest last, for the frame strip.</summary>
    public (int Ordinal, long DurationTicks)[] SnapshotStrip(int count) {
        lock (_gate) {
            int take = Math.Min(count <= 0 ? _count : count, _count);
            if (take == 0)
                return [];
            (int, long)[] strip = new (int, long)[take];
            int start = _count < _buffer.Length ? 0 : _next;
            // walk the newest `take`, so a strip narrower than the ring shows the recent end.
            for (int i = 0; i < take; i++) {
                FrameSnapshot frame = _buffer[(start + _count - take + i) % _buffer.Length];
                strip[i] = (frame.CaptureOrdinal, frame.DurationTicks);
            }
            return strip;
        }
    }

    /// <summary>
    /// Ordinals ascend but skip any frame that carried no samples, so this binary searches
    /// rather than doing Neo's offset arithmetic.
    /// </summary>
    public FrameSnapshot? FindByOrdinal(int ordinal) {
        lock (_gate) {
            int start = _count < _buffer.Length ? 0 : _next;
            int lo = 0;
            int hi = _count - 1;
            while (lo <= hi) {
                int mid = lo + ((hi - lo) / 2);
                FrameSnapshot frame = _buffer[(start + mid) % _buffer.Length];
                if (frame.CaptureOrdinal == ordinal)
                    return frame;
                if (frame.CaptureOrdinal < ordinal)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }
            return null;
        }
    }

    // first index whose ordinal is >= target. caller holds _gate.
    private int LowerBound(int start, int target) {
        int lo = 0;
        int hi = _count;
        while (lo < hi) {
            int mid = lo + ((hi - lo) / 2);
            if (_buffer[(start + mid) % _buffer.Length].CaptureOrdinal < target)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Frames from <paramref name="fromOrdinal"/> forward, ascending, at most
    /// <paramref name="count"/>. A negative from means the newest <paramref name="count"/>.
    /// </summary>
    public FrameSnapshot[] Range(int fromOrdinal, int count) {
        lock (_gate) {
            int take = Math.Min(count <= 0 ? _count : count, _count);
            if (take == 0)
                return [];
            int start = _count < _buffer.Length ? 0 : _next;
            // an evicted `from` lands on the oldest frame still held, so the run is clipped
            // rather than empty; holes inside the run just stay missing.
            int first = fromOrdinal < 0 ? Math.Max(0, _count - take) : LowerBound(start, fromOrdinal);
            int n = Math.Min(take, _count - first);
            if (n <= 0)
                return [];
            FrameSnapshot[] frames = new FrameSnapshot[n];
            for (int i = 0; i < n; i++)
                frames[i] = _buffer[(start + first + i) % _buffer.Length];
            return frames;
        }
    }

    /// <summary>
    /// Median total ticks per section over the newest <paramref name="frames"/> frames, which
    /// is what the call tree compares a row against.
    /// </summary>
    public Dictionary<int, long> BaselineMedians(int frames) {
        FrameSnapshot[] recent = SnapshotStrip(0).Length == 0 ? [] : Snapshot();
        if (recent.Length == 0)
            return [];
        int take = Math.Min(frames <= 0 ? recent.Length : frames, recent.Length);
        Dictionary<int, List<long>> perSection = [];
        Dictionary<int, long> perFrame = [];
        for (int f = recent.Length - take; f < recent.Length; f++) {
            perFrame.Clear();
            FrameSnapshot frame = recent[f];
            for (int i = 0; i < frame.NodeCount; i++) {
                int id = frame.SectionIds[i];
                perFrame[id] = perFrame.TryGetValue(id, out long sum) ? sum + frame.NodeElapsedTicks[i] : frame.NodeElapsedTicks[i];
            }
            foreach (KeyValuePair<int, long> entry in perFrame) {
                if (!perSection.TryGetValue(entry.Key, out List<long>? samples))
                    perSection[entry.Key] = samples = [];
                samples.Add(entry.Value);
            }
        }

        Dictionary<int, long> medians = new(perSection.Count);
        foreach (KeyValuePair<int, List<long>> entry in perSection) {
            List<long> samples = entry.Value;
            samples.Sort();
            medians[entry.Key] = samples[samples.Count / 2];
        }
        return medians;
    }

    public FrameRingStats ComputeStats() => StatsFor(Snapshot());

    // TODO(perf): sorts the whole ring per call, 2000 longs at a few hz. incremental
    // percentiles if the endpoint ever gets hot.
    public static FrameRingStats StatsFor(FrameSnapshot[] frames) {
        if (frames.Length == 0)
            return new FrameRingStats(0, 0, 0, 0, 0, 0, 0, -1, -1);

        long[] durations = new long[frames.Length];
        int newest = int.MinValue;
        int oldest = int.MaxValue;
        for (int i = 0; i < frames.Length; i++) {
            durations[i] = frames[i].DurationTicks;
            if (frames[i].CaptureOrdinal > newest)
                newest = frames[i].CaptureOrdinal;
            if (frames[i].CaptureOrdinal < oldest)
                oldest = frames[i].CaptureOrdinal;
        }
        Array.Sort(durations);
        // the array is already sorted, so each extra percentile is one more index
        int Pct(double q) => Math.Min(frames.Length - 1, (int)(frames.Length * q));
        return new FrameRingStats(
            frames.Length,
            durations[frames.Length / 2],
            durations[Pct(0.75)],
            durations[Pct(0.90)],
            durations[Pct(0.99)],
            durations[0],
            durations[frames.Length - 1],
            newest,
            oldest);
    }

    public void Clear() {
        lock (_gate) {
            _next = 0;
            _count = 0;
            _openOrdinal = -1;
            _preFrameSamples = 0;
            _lateSamples = 0;
            ClearOpen();
        }
    }

    private void SealOpen() {
        if (_openOrdinal < 0 || _openSectionIds.Count == 0) {
            ClearOpen();
            return;
        }

        long start = long.MaxValue;
        long end = long.MinValue;
        for (int i = 0; i < _openStartTicks.Count; i++) {
            long nodeStart = _openStartTicks[i];
            long nodeEnd = nodeStart + _openElapsedTicks[i];
            if (nodeStart < start)
                start = nodeStart;
            if (nodeEnd > end)
                end = nodeEnd;
        }

        _buffer[_next] = new FrameSnapshot(
            _openOrdinal,
            start,
            end,
            [.. _openSectionIds],
            [.. _openParentIds],
            [.. _openNodeIds],
            [.. _openParentNodeIds],
            [.. _openStartTicks],
            [.. _openElapsedTicks],
            [.. _openAllocBytes],
            // a v8 sender stamps no ids, so the lane array stays empty instead of all zeros.
            _openHasThreadIds ? [.. _openThreadIds] : []);
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
        ClearOpen();
    }

    private void ClearOpen() {
        _openSectionIds.Clear();
        _openParentIds.Clear();
        _openNodeIds.Clear();
        _openParentNodeIds.Clear();
        _openStartTicks.Clear();
        _openElapsedTicks.Clear();
        _openAllocBytes.Clear();
        _openThreadIds.Clear();
        _openHasThreadIds = false;
    }
}
