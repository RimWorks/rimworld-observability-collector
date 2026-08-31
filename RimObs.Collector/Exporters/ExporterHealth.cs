using System;
using System.Threading;

namespace RimWorks.RimObs.Collector.Exporters;

// Tracks the runtime health of the Prometheus exporter so the dashboard can
// surface enabled state, last scrape time, sample count, and the most recent
// error without coupling to a hot path (PRD §17.1).
public sealed class ExporterHealth {
    private long _totalScrapes;
    private long _lastSampleCount;
    private long _lastScrapeUtcTicks;
    private long _totalErrors;
    private volatile string? _lastError;

    public long TotalScrapes => Interlocked.Read(ref _totalScrapes);
    public long LastSampleCount => Interlocked.Read(ref _lastSampleCount);
    public long TotalErrors => Interlocked.Read(ref _totalErrors);
    public string? LastError => _lastError;

    public DateTime? LastScrapeUtc {
        get {
            long ticks = Interlocked.Read(ref _lastScrapeUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void RecordScrape(int sampleCount) {
        Interlocked.Increment(ref _totalScrapes);
        Interlocked.Exchange(ref _lastSampleCount, sampleCount);
        Interlocked.Exchange(ref _lastScrapeUtcTicks, DateTime.UtcNow.Ticks);
    }

    public void RecordError(string message) {
        Interlocked.Increment(ref _totalErrors);
        _lastError = message;
    }
}
