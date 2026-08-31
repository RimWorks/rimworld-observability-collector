using System;
using System.Threading;

namespace RimWorks.RimObs.Transport;

internal sealed class BoundedSampleQueue<T>
    where T : struct {
    private readonly object _lock = new();
    private readonly T[] _queue;
    private int _count;
    private long _dropped;

    public BoundedSampleQueue(int capacity) {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _queue = new T[capacity];
    }

    public int Capacity => _queue.Length;
    public long Dropped => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(in T sample) {
        lock (_lock) {
            if (_count >= _queue.Length) {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _queue[_count++] = sample;
            return true;
        }
    }

    public int DrainSnapshot(T[] destination) {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        lock (_lock) {
            if (_count == 0)
                return 0;
            if (destination.Length < _count)
                throw new ArgumentException("Destination buffer is smaller than queue capacity.", nameof(destination));
            int n = _count;
            Array.Copy(_queue, 0, destination, 0, n);
            _count = 0;
            return n;
        }
    }
}
