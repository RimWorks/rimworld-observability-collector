namespace RimWorks.RimObs.Collector.Aggregation;

internal sealed class BoundedRecordRing<T>
    where T : struct {
    private readonly object _lock = new();
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public BoundedRecordRing(int capacity) {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count {
        get {
            lock (_lock) {
                return _count;
            }
        }
    }

    public void Add(in T value) {
        lock (_lock) {
            _buffer[_head] = value;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }
    }

    public T[] SnapshotNewestFirst(int limit) {
        if (limit <= 0)
            return [];
        lock (_lock) {
            int take = Math.Min(limit, _count);
            T[] result = new T[take];
            int idx = (_head - 1 + _buffer.Length) % _buffer.Length;
            for (int i = 0; i < take; i++) {
                result[i] = _buffer[idx];
                idx = (idx - 1 + _buffer.Length) % _buffer.Length;
            }
            return result;
        }
    }
}
