using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RimWorks.RimObs.Transport;

internal sealed class SampleRingBuffer {
    internal struct Slot {
        public int SectionId;
        public int ParentId;
        public long StartTimestamp;
        public long ElapsedTicks;
        public int FrameOrdinal;
        public int NodeId;
        public int ParentNodeId;
        public long AllocBytes;
        public int ThreadId;
        public long Sequence;
    }

    private readonly Slot[] _slots;
    private readonly int _mask;
    private long _claim;
    private long _read;
    private long _dropped;

    public SampleRingBuffer(int capacity) {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a positive power of two.", nameof(capacity));
        _slots = new Slot[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _slots.Length;
    public long Dropped => Interlocked.Read(ref _dropped);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, int frameOrdinal, long allocBytes = 0L) {
        // one producer per ring, so the claim is plain - and it must not advance on the drop
        // path, or the drain stops at the unpublished gap forever.
        long seq = _claim + 1;
        long read = Volatile.Read(ref _read);
        if (seq - read > _slots.Length) {
            Interlocked.Increment(ref _dropped);
            return false;
        }
        _claim = seq;
        int idx = (int)((seq - 1) & _mask);
        _slots[idx].SectionId = sectionId;
        _slots[idx].ParentId = parentId;
        _slots[idx].StartTimestamp = startTimestamp;
        _slots[idx].ElapsedTicks = elapsedTicks;
        _slots[idx].FrameOrdinal = frameOrdinal;
        _slots[idx].NodeId = nodeId;
        _slots[idx].ParentNodeId = parentNodeId;
        _slots[idx].AllocBytes = allocBytes;
        // TryWrite always runs on the producing thread, so the lane is whoever is calling.
        _slots[idx].ThreadId = Environment.CurrentManagedThreadId;
        Volatile.Write(ref _slots[idx].Sequence, seq);
        return true;
    }

    public int Drain(SampleBatch batch, int maxCount) {
        int n = 0;
        long expected = _read + 1;
        int cap = Math.Min(maxCount, batch.Capacity);
        while (n < cap) {
            int idx = (int)((expected - 1) & _mask);
            if (Volatile.Read(ref _slots[idx].Sequence) != expected)
                break;
            batch.SectionIds[n] = _slots[idx].SectionId;
            batch.ParentIds[n] = _slots[idx].ParentId;
            batch.StartTimestamps[n] = _slots[idx].StartTimestamp;
            batch.ElapsedTicks[n] = _slots[idx].ElapsedTicks;
            batch.FrameOrdinals[n] = _slots[idx].FrameOrdinal;
            batch.NodeIds[n] = _slots[idx].NodeId;
            batch.ParentNodeIds[n] = _slots[idx].ParentNodeId;
            batch.AllocBytes[n] = _slots[idx].AllocBytes;
            batch.ThreadIds[n] = _slots[idx].ThreadId;
            n++;
            expected++;
        }
        if (n > 0)
            Volatile.Write(ref _read, expected - 1);
        return n;
    }

    /// <summary>
    /// Throws away every published sample still in the ring and returns how many there were.
    /// Only for a lane being reaped, where there is nowhere left to put them.
    /// </summary>
    public int DiscardAll() {
        int n = 0;
        long expected = _read + 1;
        while (Volatile.Read(ref _slots[(int)((expected - 1) & _mask)].Sequence) == expected) {
            n++;
            expected++;
        }
        if (n > 0)
            Volatile.Write(ref _read, expected - 1);
        return n;
    }
}

/// <summary>
/// One <see cref="SampleRingBuffer"/> per producing thread, so a runaway worker fills and drops
/// its own lane instead of eating the capacity the main thread needs.
/// </summary>
internal sealed class SampleRingSet {
    private sealed class Lane {
        public Lane(SampleRingBuffer ring, Thread owner) {
            Ring = ring;
            Owner = owner;
        }

        public SampleRingBuffer Ring { get; }
        public Thread Owner { get; }
    }

    private readonly int _laneCapacity;
    private readonly ThreadLocal<SampleRingBuffer> _lane;
    private Lane[] _lanes = Array.Empty<Lane>();
    private int _cursor;
    private long _reapedDropped;

    public SampleRingSet(int laneCapacity) {
        _laneCapacity = laneCapacity;
        _lane = new ThreadLocal<SampleRingBuffer>(AddLane);
    }

    /// <summary>
    /// Called on the draining thread when a dead thread's lane is dropped, so a consumer can forget
    /// state it keyed on that managed thread id. The runtime hands recycled ids to new threads.
    /// </summary>
    public Action<int>? LaneReaped { get; set; }

    public int LaneCapacity => _laneCapacity;
    public int LaneCount => Volatile.Read(ref _lanes).Length;

    public long Dropped {
        get {
            Lane[] lanes = Volatile.Read(ref _lanes);
            long total = Interlocked.Read(ref _reapedDropped);
            for (int i = 0; i < lanes.Length; i++)
                total += lanes[i].Ring.Dropped;
            return total;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, int frameOrdinal, long allocBytes = 0L) {
        return _lane.Value!.TryWrite(sectionId, parentId, nodeId, parentNodeId, startTimestamp, elapsedTicks, frameOrdinal, allocBytes);
    }

    /// <summary>
    /// Drains the next non-empty lane, round-robin, so a hot lane cannot starve the others.
    /// Returns 0 only when every lane is empty. Drops a drained lane whose owner has exited.
    /// </summary>
    public int Drain(SampleBatch batch, int maxCount) {
        Lane[] lanes = Volatile.Read(ref _lanes);
        for (int i = 0; i < lanes.Length; i++) {
            int idx = (_cursor + i) % lanes.Length;
            Lane lane = lanes[idx];
            int n = lane.Ring.Drain(batch, maxCount);
            if (n > 0) {
                _cursor = (idx + 1) % lanes.Length;
                return n;
            }
            if (!lane.Owner.IsAlive) {
                int last = Reap(lane, batch, maxCount);
                if (last > 0) {
                    _cursor = (idx + 1) % lanes.Length;
                    return last;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Name of the thread that owns the lane, or empty if it is unnamed or already reaped.
    /// </summary>
    public string NameFor(int threadId) {
        Lane[] lanes = Volatile.Read(ref _lanes);
        for (int i = 0; i < lanes.Length; i++) {
            if (lanes[i].Owner.ManagedThreadId == threadId)
                return lanes[i].Owner.Name ?? string.Empty;
        }
        return string.Empty;
    }

    private SampleRingBuffer AddLane() {
        SampleRingBuffer ring = new(_laneCapacity);
        Lane lane = new(ring, Thread.CurrentThread);
        Lane[] old;
        Lane[] grown;
        do {
            old = Volatile.Read(ref _lanes);
            grown = new Lane[old.Length + 1];
            Array.Copy(old, grown, old.Length);
            grown[old.Length] = lane;
        }
        while (Interlocked.CompareExchange(ref _lanes, grown, old) != old);
        return ring;
    }

    /// <summary>
    /// Drains a dead thread's lane one last time, since the owner can publish between the empty
    /// drain and the IsAlive check. Removes it only once it hands over nothing, so NameFor still resolves.
    /// </summary>
    private int Reap(Lane lane, SampleBatch batch, int maxCount) {
        int taken = lane.Ring.Drain(batch, maxCount);
        if (taken > 0)
            return taken;

        Lane[] old;
        Lane[] shrunk;
        do {
            old = Volatile.Read(ref _lanes);
            int at = Array.IndexOf(old, lane);
            if (at < 0)
                return 0;
            shrunk = new Lane[old.Length - 1];
            Array.Copy(old, shrunk, at);
            Array.Copy(old, at + 1, shrunk, at, old.Length - at - 1);
        }
        while (Interlocked.CompareExchange(ref _lanes, shrunk, old) != old);
        Interlocked.Add(ref _reapedDropped, lane.Ring.Dropped + lane.Ring.DiscardAll());
        LaneReaped?.Invoke(lane.Owner.ManagedThreadId);
        return 0;
    }
}
