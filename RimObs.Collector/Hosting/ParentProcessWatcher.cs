using System.Diagnostics;
using RimWorks.RimObs.Collector.Aggregation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RimWorks.RimObs.Collector.Hosting;

public sealed class ParentProcessWatcher : BackgroundService {
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private readonly int _parentPid;
    private readonly SessionAggregator _aggregator;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ParentProcessWatcher> _log;
    private readonly DateTime _startUtc = DateTime.UtcNow;

    public ParentProcessWatcher(
        ServeOptions options,
        SessionAggregator aggregator,
        IHostApplicationLifetime lifetime,
        ILogger<ParentProcessWatcher> log) {
        ArgumentNullException.ThrowIfNull(options);
        _parentPid = options.ParentPid;
        _aggregator = aggregator;
        _lifetime = lifetime;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (_parentPid <= 0)
            return;

        _log.LogInformation("Watching parent process {ParentPid}; will shut down when it exits or after {IdleTimeout} idle.", _parentPid, IdleTimeout);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                return;
            }

            bool parentAlive = IsProcessAlive(_parentPid);
            DateTime lastActivity = _aggregator.LastBatchUtc == default ? _startUtc : _aggregator.LastBatchUtc;
            TimeSpan sinceLastActivity = DateTime.UtcNow - lastActivity;

            if (LifecycleDecision.ShouldShutdown(true, parentAlive, sinceLastActivity, IdleTimeout)) {
                string reason = parentAlive ? $"no telemetry for {sinceLastActivity.TotalSeconds:F0}s" : $"parent process {_parentPid} exited";
                _log.LogInformation("Shutting down collector: {Reason}.", reason);
                _lifetime.StopApplication();
                return;
            }
        }
    }

    private static bool IsProcessAlive(int pid) {
        try {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }
}
