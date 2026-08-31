using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace RimWorks.RimObs.Collector.Captures;

public sealed class CaptureTimeCapWatcher : BackgroundService {
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly CaptureManager _captures;

    public CaptureTimeCapWatcher(CaptureManager captures) {
        _captures = captures;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                return;
            }

            _captures.EnforceTimeCap();
        }
    }
}
