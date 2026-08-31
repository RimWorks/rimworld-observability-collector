using RimWorks.RimObs.Collector.Aggregation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RimWorks.RimObs.Collector.Storage;

public sealed class PersistenceFlusher : BackgroundService {
    public static TimeSpan DefaultInterval { get; } = TimeSpan.FromSeconds(5);
    public const int GcSnapshotLimit = 1024;

    private readonly SessionAggregator _aggregator;
    private readonly ISessionPersister _persister;
    private readonly ILogger<PersistenceFlusher>? _logger;
    private readonly TimeSpan _interval;

    public PersistenceFlusher(SessionAggregator aggregator, ISessionPersister persister, ILogger<PersistenceFlusher>? logger = null)
        : this(aggregator, persister, DefaultInterval, logger) {
    }

    public PersistenceFlusher(SessionAggregator aggregator, ISessionPersister persister, TimeSpan interval, ILogger<PersistenceFlusher>? logger = null) {
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(persister);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "interval must be positive");

        _aggregator = aggregator;
        _persister = persister;
        _interval = interval;
        _logger = logger;
    }

    public void FlushOnce() {
        string? sessionId = _aggregator.Meta?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        IReadOnlyCollection<SectionStats> sections = _aggregator.Sections;
        IReadOnlyCollection<MetricStats> metrics = _aggregator.Metrics;
        IReadOnlyCollection<CallEdgeStats> callEdges = _aggregator.CallEdges;
        GcEventRecord[] gc = _aggregator.SnapshotGcEvents(GcSnapshotLimit);

        if (sections.Count > 0)
            _persister.WriteSectionsSnapshot(sessionId, sections);
        if (metrics.Count > 0)
            _persister.WriteMetricsSnapshot(sessionId, metrics);
        if (callEdges.Count > 0)
            _persister.WriteCallTreeSnapshot(sessionId, callEdges);
        _persister.ReplaceGcEventsSnapshot(sessionId, gc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(_interval);
        while (!stoppingToken.IsCancellationRequested) {
            try {
                FlushOnce();
            }
            catch (Exception ex) {
                _logger?.LogWarning(ex, "Persistence flush failed; continuing");
            }

            try {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        try {
            FlushOnce();
        }
        catch (Exception ex) {
            _logger?.LogWarning(ex, "Final persistence flush on shutdown failed");
        }
    }


}
