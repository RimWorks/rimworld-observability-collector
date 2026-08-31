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
    public bool TryWrite(int sectionId, int parentId, long startTimestamp, long elapsedTicks) {
        long seq = Interlocked.Increment(ref _claim);
        long read = Volatile.Read(ref _read);
        if (seq - read > _slots.Length) {
            Interlocked.Increment(ref _dropped);
            return false;
        }
        int idx = (int)((seq - 1) & _mask);
        _slots[idx].SectionId = sectionId;
        _slots[idx].ParentId = parentId;
        _slots[idx].StartTimestamp = startTimestamp;
        _slots[idx].ElapsedTicks = elapsedTicks;
        Volatile.Write(ref _slots[idx].Sequence, seq);
        return true;
    }

    public int Drain(int[] sectionIds, int[] parentIds, long[] startTimestamps, long[] elapsedTicks, int maxCount) {
        int n = 0;
        long expected = _read + 1;
        int cap = Math.Min(maxCount, Math.Min(sectionIds.Length, Math.Min(parentIds.Length, Math.Min(startTimestamps.Length, elapsedTicks.Length))));
        while (n < cap) {
            int idx = (int)((expected - 1) & _mask);
            if (Volatile.Read(ref _slots[idx].Sequence) != expected)
                break;
            sectionIds[n] = _slots[idx].SectionId;
            parentIds[n] = _slots[idx].ParentId;
            startTimestamps[n] = _slots[idx].StartTimestamp;
            elapsedTicks[n] = _slots[idx].ElapsedTicks;
            n++;
            expected++;
        }
        if (n > 0)
            Volatile.Write(ref _read, expected - 1);
        return n;
    }
}
