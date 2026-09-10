using System;
using System.Collections.Generic;
using System.Linq;

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
/// Frames cut out of the sample stream by their ordinal, newest last. The library drains one
/// thread lane per call, so a frame stays open until <see cref="OpenFrameWindow"/> newer ones land.
/// </summary>
public sealed class FrameRing {
    public const int DefaultCapacity = 2000;

    /// <summary>
    /// How far behind the newest ordinal a frame seals. One sender flush covers about six
    /// frames per lane, so this leaves every lane room to report before the cut.
    /// </summary>
    public const int OpenFrameWindow = 16;

    private FrameSnapshot[] _buffer;
    private readonly object _gate = new();
    private readonly SortedDictionary<int, OpenFrame> _open = [];
    private readonly int _window;
    private int _next;
    private int _count;
    private int _newestOrdinal;
    private int _sealedThrough;
    private long _preFrameSamples;
    private long _lateSamples;

    public FrameRing()
        : this(DefaultCapacity) {
    }

    public FrameRing(int capacity, int openFrameWindow = OpenFrameWindow) {
        _buffer = new FrameSnapshot[Math.Max(1, capacity)];
        _window = Math.Max(1, openFrameWindow);
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
            if (frameOrdinal <= _sealedThrough) {
                _lateSamples++;
                return;
            }
            if (!_open.TryGetValue(frameOrdinal, out OpenFrame? open))
                _open[frameOrdinal] = open = new OpenFrame();
            open.SectionIds.Add(sectionId);
            open.ParentIds.Add(parentId);
            open.NodeIds.Add(nodeId);
            open.ParentNodeIds.Add(parentNodeId);
            open.StartTicks.Add(startTicks);
            open.ElapsedTicks.Add(elapsedTicks);
            open.AllocBytes.Add(allocBytes);
            open.ThreadIds.Add(threadId);
            if (threadId != 0)
                open.HasThreadIds = true;
            if (frameOrdinal > _newestOrdinal) {
                _newestOrdinal = frameOrdinal;
                SealThrough(_newestOrdinal - _window);
            }
        }
    }

    /// <summary>Seals every open frame, so a stopped stream still lands its newest frames.</summary>
    public void Flush() {
        lock (_gate) {
            SealThrough(int.MaxValue);
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
            _newestOrdinal = 0;
            _sealedThrough = 0;
            _preFrameSamples = 0;
            _lateSamples = 0;
            _open.Clear();
        }
    }

    // seals open frames up to and including `watermark`, oldest first so the ring stays ascending.
    private void SealThrough(int watermark) {
        while (_open.Count > 0) {
            int ordinal = _open.Keys.First();
            if (ordinal > watermark)
                return;
            OpenFrame open = _open[ordinal];
            _open.Remove(ordinal);
            _sealedThrough = ordinal;
            Seal(ordinal, open);
        }
    }

    private void Seal(int ordinal, OpenFrame open) {
        if (open.SectionIds.Count == 0)
            return;

        long start = long.MaxValue;
        long end = long.MinValue;
        for (int i = 0; i < open.StartTicks.Count; i++) {
            long nodeStart = open.StartTicks[i];
            long nodeEnd = nodeStart + open.ElapsedTicks[i];
            if (nodeStart < start)
                start = nodeStart;
            if (nodeEnd > end)
                end = nodeEnd;
        }

        _buffer[_next] = new FrameSnapshot(
            ordinal,
            start,
            end,
            [.. open.SectionIds],
            [.. open.ParentIds],
            [.. open.NodeIds],
            [.. open.ParentNodeIds],
            [.. open.StartTicks],
            [.. open.ElapsedTicks],
            [.. open.AllocBytes],
            // a v8 sender stamps no ids, so the lane array stays empty instead of all zeros.
            open.HasThreadIds ? [.. open.ThreadIds] : []);
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
    }

    private sealed class OpenFrame {
        public readonly List<int> SectionIds = [];
        public readonly List<int> ParentIds = [];
        public readonly List<int> NodeIds = [];
        public readonly List<int> ParentNodeIds = [];
        public readonly List<long> StartTicks = [];
        public readonly List<long> ElapsedTicks = [];
        public readonly List<long> AllocBytes = [];
        public readonly List<int> ThreadIds = [];
        public bool HasThreadIds;
    }
}
