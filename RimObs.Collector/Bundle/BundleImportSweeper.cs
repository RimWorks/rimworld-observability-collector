using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace RimWorks.RimObs.Collector.Bundle;

public sealed class BundleImportSweeper : BackgroundService {
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private readonly BundleImportRegistry _registry;

    public BundleImportSweeper(BundleImportRegistry registry) {
        _registry = registry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                _registry.SweepIdle();
            }
            catch { /* never throw out of the sweep loop */ }
            try {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (TaskCanceledException) {
                break;
            }
        }
        _registry.RemoveAll();
    }
}
