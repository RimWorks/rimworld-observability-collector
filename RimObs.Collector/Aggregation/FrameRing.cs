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
    long[] NodeElapsedTicks) {
    public int NodeCount => SectionIds.Length;

    public long DurationTicks => EndTicks - StartTicks;
}

public sealed record FrameRingStats(
    int FrameCount,
    long MedianDurationTicks,
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

    private readonly FrameSnapshot[] _buffer;
    private readonly object _gate = new();
    private readonly List<int> _openSectionIds = [];
    private readonly List<int> _openParentIds = [];
    private readonly List<int> _openNodeIds = [];
    private readonly List<int> _openParentNodeIds = [];
    private readonly List<long> _openStartTicks = [];
    private readonly List<long> _openElapsedTicks = [];
    private int _next;
    private int _count;
    private int _openOrdinal = -1;
    private long _preFrameSamples;
    private long _lateSamples;

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

    public void Add(int frameOrdinal, int sectionId, int parentId, int nodeId, int parentNodeId, long startTicks, long elapsedTicks) {
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

    // TODO(perf): sorts the whole ring per call, 2000 longs at a few hz. incremental
    // percentiles if the endpoint ever gets hot.
    public FrameRingStats ComputeStats() {
        lock (_gate) {
            if (_count == 0)
                return new FrameRingStats(0, 0, 0, 0, 0, -1, -1);

            long[] durations = new long[_count];
            int start = _count < _buffer.Length ? 0 : _next;
            int newest = int.MinValue;
            int oldest = int.MaxValue;
            for (int i = 0; i < _count; i++) {
                FrameSnapshot frame = _buffer[(start + i) % _buffer.Length];
                durations[i] = frame.DurationTicks;
                if (frame.CaptureOrdinal > newest)
                    newest = frame.CaptureOrdinal;
                if (frame.CaptureOrdinal < oldest)
                    oldest = frame.CaptureOrdinal;
            }
            Array.Sort(durations);
            int p99 = Math.Min(_count - 1, (int)(_count * 0.99));
            return new FrameRingStats(
                _count,
                durations[_count / 2],
                durations[p99],
                durations[0],
                durations[_count - 1],
                newest,
                oldest);
        }
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
            [.. _openElapsedTicks]);
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
    }
}
