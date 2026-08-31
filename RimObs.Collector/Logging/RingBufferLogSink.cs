using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace RimWorks.RimObs.Collector.Logging;

public sealed class RingBufferLogSink : ILogEventSink {
    public const int DefaultCapacity = 1024;

    private readonly int _capacity;
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public RingBufferLogSink() : this(DefaultCapacity) {
    }

    public RingBufferLogSink(int capacity) {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive");
        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public int Count => _entries.Count;

    public void Emit(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        string message = logEvent.RenderMessage();
        string? exception = logEvent.Exception?.ToString();
        LogEntry entry = new(logEvent.Timestamp, logEvent.Level, message, exception);

        _entries.Enqueue(entry);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _)) {
            // Eviction happens in the loop condition; nothing to do per iteration.
        }
    }

    public IReadOnlyList<LogEntry> Snapshot(string? minLevel = null, int limit = 200) {
        if (limit <= 0)
            return Array.Empty<LogEntry>();

        LogEventLevel? minLevelEnum = null;
        if (!string.IsNullOrWhiteSpace(minLevel) && Enum.TryParse<LogEventLevel>(minLevel, ignoreCase: true, out LogEventLevel parsed))
            minLevelEnum = parsed;

        LogEntry[] snapshot = _entries.ToArray();
        List<LogEntry> filtered = new(Math.Min(snapshot.Length, limit));
        for (int i = snapshot.Length - 1; i >= 0 && filtered.Count < limit; i--) {
            LogEntry entry = snapshot[i];
            if (minLevelEnum is null || entry.Level >= minLevelEnum.Value)
                filtered.Add(entry);
        }

        return filtered;
    }
}
