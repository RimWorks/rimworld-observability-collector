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
/// Frames cut out of the sample stream by their ordinal, newest last. A frame stays open until
/// <see cref="OpenFrameWindow"/> newer ones land, so a lane draining late can still add to it,
/// but every read serves the open frames too, rebuilt fresh whenever new samples arrived.
/// </summary>
public sealed class FrameRing {
    public const int DefaultCapacity = 2000;

    /// <summary>
    /// How far behind the newest ordinal a frame seals. One sender flush covers about six
    /// frames per lane, so this leaves every lane room to report before the cut.
    /// </summary>
    public const int DefaultOpenFrameWindow = 16;

    /// <summary>
    /// How long without a sample means the stream stopped rather than paused between drains.
    /// The sender flushes every 100ms, so this is two and a half drains of slack.
    /// </summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(250);

    /// <summary>Ceiling on the fps-derived floor, so a bogus fps report cannot pin the ring open.</summary>
    public const int MaxFpsWindow = 256;

    private FrameSnapshot[] _buffer;
    private readonly object _gate = new();
    private readonly SortedDictionary<int, OpenFrame> _open = [];
    private int _window = DefaultOpenFrameWindow;
    private int _fpsFloor;
    private int _next;
    private int _count;
    private int _newestOrdinal;
    private int _sealedThrough;
    private int _servedOrdinal;
    private long _preFrameSamples;
    private long _lateSamples;
    private long _lastSampleStamp;

    public FrameRing(int capacity = DefaultCapacity) {
        _buffer = new FrameSnapshot[Math.Max(1, capacity)];
    }

    public int Capacity => _buffer.Length;

    /// <summary>Time source for the quiet check. Swappable so tests can drive it.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Silence after which <see cref="Latest"/> stops holding the newest frame back.</summary>
    public TimeSpan QuietPeriod { get; set; } = DefaultQuietPeriod;

    /// <summary>How many newer ordinals must land before a frame seals. Live setting, floors at 1.</summary>
    public int OpenFrameWindow {
        get {
            lock (_gate) {
                return _window;
            }
        }

        set {
            lock (_gate) {
                _window = Math.Max(1, value);
            }
        }
    }

    /// <summary>The window actually applied: the configured ordinals or the fps floor, whichever is wider.</summary>
    public int EffectiveOpenFrameWindow {
        get {
            lock (_gate) {
                return Math.Max(_window, _fpsFloor);
            }
        }
    }

    /// <summary>
    /// Scales the open window to the reported render rate. The configured window is ordinals
    /// but drains are wall clock, so at uncapped fps it must widen to stay ~250ms of real time.
    /// </summary>
    public void NoteFps(double fps) {
        int floor = fps > 0 ? (int)(fps / 4.0) : 0;
        lock (_gate) {
            _fpsFloor = Math.Min(floor, MaxFpsWindow);
        }
    }

    public int Count {
        get {
            lock (_gate) {
                return _count + _open.Count;
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

    public void Add(int frameOrdinal, int sectionId, int parentId, int nodeId, int parentNodeId, long startTicks, long elapsedTicks, long allocBytes = 0L, int threadId = 0, bool mainLane = true) {
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
            open.HasMain |= mainLane;
            open.SectionIds.Add(sectionId);
            open.ParentIds.Add(parentId);
            open.NodeIds.Add(nodeId);
            open.ParentNodeIds.Add(parentNodeId);
            open.StartTicks.Add(startTicks);
            open.ElapsedTicks.Add(elapsedTicks);
            open.AllocBytes.Add(allocBytes);
            open.ThreadIds.Add(threadId);
            _lastSampleStamp = Clock.GetTimestamp();
            if (threadId != 0)
                open.HasThreadIds = true;
            if (frameOrdinal > _newestOrdinal) {
                _newestOrdinal = frameOrdinal;
                SealThrough(_newestOrdinal - Math.Max(_window, _fpsFloor));
            }
        }
    }

    /// <summary>Seals every open frame, for teardown. The only reader-facing way to move the watermark.</summary>
    public void Flush() {
        lock (_gate) {
            SealThrough(int.MaxValue);
        }
    }

    /// <summary>
    /// The newest frame that has had a full drain cycle: a whole open window behind the newest
    /// while samples keep arriving, the true newest once the stream goes quiet.
    /// </summary>
    public FrameSnapshot? Latest() {
        lock (_gate) {
            bool live = _lastSampleStamp != 0 && Clock.GetElapsedTime(_lastSampleStamp) < QuietPeriod;
            // the same watermark SealThrough uses: a frame any lane can still add to is not
            // servable, because the dashboard never backfills an ordinal it has already drawn.
            int ceiling = live ? _newestOrdinal - Math.Max(_window, _fpsFloor) : int.MaxValue;
            // a quiet gap serves an open frame, and the next sample drops the ceiling back under
            // it. never walk backwards, or the dashboard freezes on that truncated frame forever.
            if (ceiling < _servedOrdinal)
                ceiling = _servedOrdinal;
            FrameSnapshot? newest = null;
            foreach (KeyValuePair<int, OpenFrame> entry in _open) {
                if (entry.Key > ceiling)
                    break;
                if (!entry.Value.HasMain)
                    continue;
                newest = entry.Value.Materialize(entry.Key);
            }
            newest ??= _count == 0 ? null : _buffer[(_next - 1 + _buffer.Length) % _buffer.Length];
            if (newest is not null && newest.CaptureOrdinal > _servedOrdinal)
                _servedOrdinal = newest.CaptureOrdinal;
            return newest;
        }
    }

    public FrameSnapshot[] Snapshot() {
        lock (_gate) {
            return View();
        }
    }

    /// <summary>
    /// Swaps in a buffer of a new size, keeping the newest frames that still fit. Shrinking
    /// drops the oldest; the open frames and the drop counters survive either way.
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
            FrameSnapshot[] view = View();
            int take = Math.Min(count <= 0 ? view.Length : count, view.Length);
            if (take == 0)
                return [];
            (int, long)[] strip = new (int, long)[take];
            // walk the newest `take`, so a strip narrower than the ring shows the recent end.
            for (int i = 0; i < take; i++) {
                FrameSnapshot frame = view[view.Length - take + i];
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
            if (_open.TryGetValue(ordinal, out OpenFrame? open))
                return open.HasMain ? open.Materialize(ordinal) : null;
            FrameSnapshot[] view = View();
            int at = LowerBound(view, ordinal);
            if (at < view.Length && view[at].CaptureOrdinal == ordinal)
                return view[at];
            return null;
        }
    }

    // first index whose ordinal is >= target.
    private static int LowerBound(FrameSnapshot[] frames, int target) {
        int lo = 0;
        int hi = frames.Length;
        while (lo < hi) {
            int mid = lo + ((hi - lo) / 2);
            if (frames[mid].CaptureOrdinal < target)
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
            FrameSnapshot[] view = View();
            int take = Math.Min(count <= 0 ? view.Length : count, view.Length);
            if (take == 0)
                return [];
            // an evicted `from` lands on the oldest frame still held, so the run is clipped
            // rather than empty; holes inside the run just stay missing.
            int first = fromOrdinal < 0 ? Math.Max(0, view.Length - take) : LowerBound(view, fromOrdinal);
            int n = Math.Min(take, view.Length - first);
            if (n <= 0)
                return [];
            return view[first..(first + n)];
        }
    }

    /// <summary>
    /// Median total ticks per section over the newest <paramref name="frames"/> frames, which
    /// is what the call tree compares a row against.
    /// </summary>
    public Dictionary<int, long> BaselineMedians(int frames) {
        FrameSnapshot[] recent = Snapshot();
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
            _servedOrdinal = 0;
            _preFrameSamples = 0;
            _lateSamples = 0;
            _lastSampleStamp = 0;
            _open.Clear();
        }
    }

    // every open ordinal sits above _sealedThrough and every ring ordinal at or below it, so
    // sealed frames plus open previews concatenate into one ascending run. caller holds _gate.
    private FrameSnapshot[] View() {
        int openTake = 0;
        foreach (OpenFrame open in _open.Values) {
            if (open.HasMain)
                openTake++;
        }
        FrameSnapshot[] frames = new FrameSnapshot[_count + openTake];
        int start = _count < _buffer.Length ? 0 : _next;
        for (int i = 0; i < _count; i++)
            frames[i] = _buffer[(start + i) % _buffer.Length];
        int n = _count;
        foreach (KeyValuePair<int, OpenFrame> entry in _open) {
            if (!entry.Value.HasMain)
                continue;
            frames[n++] = entry.Value.Materialize(entry.Key);
        }
        return frames;
    }

    // commits open frames up to `watermark` into the ring, oldest first. the only writer of
    // _sealedThrough, so a read can never turn a lane's pending drain late.
    private void SealThrough(int watermark) {
        while (_open.Count > 0) {
            int ordinal = _open.Keys.First();
            if (ordinal > watermark)
                return;
            OpenFrame open = _open[ordinal];
            _open.Remove(ordinal);
            _sealedThrough = ordinal;
            // a frame whose main lane never landed is an anomaly, and navigation is anchored
            // on main; its stray worker samples count as late rather than becoming a frame.
            if (!open.HasMain) {
                _lateSamples += open.SectionIds.Count;
                continue;
            }
            _buffer[_next] = open.Materialize(ordinal);
            _next = (_next + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }
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

        /// <summary>False until the main lane lands; a frame without it is mid-flight.</summary>
        public bool HasMain;

        private FrameSnapshot? _snapshot;

        // cached until the next sample lands, so steady reads reuse one snapshot.
        public FrameSnapshot Materialize(int ordinal) {
            if (_snapshot is not null && _snapshot.NodeCount == SectionIds.Count)
                return _snapshot;

            long start = long.MaxValue;
            long end = long.MinValue;
            for (int i = 0; i < StartTicks.Count; i++) {
                long nodeStart = StartTicks[i];
                long nodeEnd = nodeStart + ElapsedTicks[i];
                if (nodeStart < start)
                    start = nodeStart;
                if (nodeEnd > end)
                    end = nodeEnd;
            }

            return _snapshot = new FrameSnapshot(
                ordinal,
                start,
                end,
                [.. SectionIds],
                [.. ParentIds],
                [.. NodeIds],
                [.. ParentNodeIds],
                [.. StartTicks],
                [.. ElapsedTicks],
                [.. AllocBytes],
                // a v8 sender stamps no ids, so the lane array stays empty instead of all zeros.
                HasThreadIds ? [.. ThreadIds] : []);
        }
    }
}
