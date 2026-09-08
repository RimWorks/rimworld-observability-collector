using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Collector.Instrumentation;

public sealed class DynamicPatchReplayer {
    private readonly DynamicPatchStore _store;

    public DynamicPatchReplayer(DynamicPatchStore store) {
        _store = store;
    }

    /// <summary>Rows still worth another attempt, because the game may not have been ready yet.</summary>
    public bool HasPendingRows() {
        foreach (DynamicPatchRow row in _store.List()) {
            if (row.LastStatus == PatchStatus.Pending)
                return true;
        }
        return false;
    }

    /// <summary>Call once per session: the game renumbers every patch id when it restarts.</summary>
    public void ForgetLiveIds() => _store.ClearLivePatchIds();

    public async Task ReplayAsync(ControlClient client) {
        foreach (DynamicPatchRow row in _store.List()) {
            if (row.LastStatus == PatchStatus.Active && row.LivePatchId is not null)
                continue;

            string[] paramTypes = row.ParamTypesJoined.Length == 0
                ? []
                : row.ParamTypesJoined.Split(';');

            try {
                ControlPatchResponse res = await client.PatchAsync(new ControlPatchRequest {
                    TypeFullName = row.TypeFullName,
                    MethodName = row.MethodName,
                    ParamTypeFullNames = paramTypes,
                });
                if (res.Status == PatchStatus.Active) {
                    _store.UpdateStatus(row.Id, PatchStatus.Active, null);
                    _store.UpdateLivePatchId(row.Id, res.PatchId);
                }
                else {
                    _store.UpdateStatus(row.Id, PatchStatus.Stale, res.ErrorReason);
                }
            }
            catch (ControlClientException ex) when (ex.Status == 504) {
                // the game has not drained its queue yet, usually because no map is loaded.
                // leave the row pending so the next sweep tries again.
                _store.UpdateStatus(row.Id, PatchStatus.Pending, ex.Message);
            }
            catch (System.Exception ex) {
                _store.UpdateStatus(row.Id, PatchStatus.Stale, ex.Message);
            }
        }
    }
}
