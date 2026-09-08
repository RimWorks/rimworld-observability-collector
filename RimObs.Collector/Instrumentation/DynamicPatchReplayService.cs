using RimWorks.RimObs.Collector.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RimWorks.RimObs.Collector.Instrumentation;

/// <summary>
/// Reapplies the stored patch set once a game session appears, then keeps retrying the rows that
/// have not landed yet, because the game cannot apply a patch until it is running a map.
/// </summary>
public sealed class DynamicPatchReplayService : BackgroundService {
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(3);

    private readonly SessionMetaRegistry _registry;
    private readonly DynamicPatchStore _store;
    private readonly ILogger<DynamicPatchReplayService> _log;
    private string _sessionId = string.Empty;

    public DynamicPatchReplayService(
        SessionMetaRegistry registry,
        DynamicPatchStore store,
        ILogger<DynamicPatchReplayService> log) {
        _registry = registry;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) {
                return;
            }
            await SweepAsync();
        }
    }

    internal async Task SweepAsync() {
        if (!_registry.IsAvailable || _registry.SessionId.Length == 0)
            return;

        DynamicPatchReplayer replayer = new(_store);
        bool freshSession = _registry.SessionId != _sessionId;
        if (freshSession) {
            _sessionId = _registry.SessionId;
            replayer.ForgetLiveIds();
        }
        else if (!replayer.HasPendingRows()) {
            return;
        }

        try {
            await replayer.ReplayAsync(new ControlClient(_registry.ControlPort, _registry.ControlSecret));
        }
        catch (Exception ex) {
            _log.LogWarning(ex, "replaying stored patches failed for session {SessionId}", _sessionId);
        }
    }
}
