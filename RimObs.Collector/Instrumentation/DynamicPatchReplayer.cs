using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Collector.Instrumentation;

public sealed class DynamicPatchReplayer {
    private readonly DynamicPatchStore _store;

    public DynamicPatchReplayer(DynamicPatchStore store) {
        _store = store;
    }

    public async Task ReplayAsync(ControlClient client) {
        foreach (DynamicPatchRow row in _store.List()) {
            string[] paramTypes = row.ParamTypesJoined.Length == 0
                ? []
                : row.ParamTypesJoined.Split(';');

            try {
                ControlPatchResponse res = await client.PatchAsync(new ControlPatchRequest {
                    TypeFullName = row.TypeFullName,
                    MethodName = row.MethodName,
                    ParamTypeFullNames = paramTypes,
                });
                if (res.Status == PatchStatus.Active)
                    _store.UpdateStatus(row.Id, PatchStatus.Active, null);
                else
                    _store.UpdateStatus(row.Id, PatchStatus.Stale, res.ErrorReason);
            }
            catch (System.Exception ex) {
                _store.UpdateStatus(row.Id, PatchStatus.Stale, ex.Message);
            }
        }
    }
}
