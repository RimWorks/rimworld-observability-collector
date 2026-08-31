using RimWorks.RimObs.Api;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Session;

namespace RimWorks.RimObs.Settings;

public static class CollectorStatusProvider {
    public static CollectorStatus CaptureCurrent() {
        int coreInstalled = 0;
        int coreTotal = 0;
        int declaredInstalled = 0;
        int declaredTotal = 0;
        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (entry.Declared) {
                declaredTotal++;
                if (entry.Installed)
                    declaredInstalled++;
            }
            else {
                coreTotal++;
                if (entry.Installed)
                    coreInstalled++;
            }
        }

        ControlServer? server = ControlServices.Server;
        int controlPort = server is null ? 0 : server.Port;

        string sessionId = SessionAnchor.IsInitialized ? SessionAnchor.SessionId : string.Empty;

        return new CollectorStatus(
            CollectorRuntimeInfo.CollectorRunning,
            CollectorRuntimeInfo.LaunchAttempted,
            CollectorRuntimeInfo.Host,
            CollectorRuntimeInfo.Port,
            controlPort,
            Profiler.Enabled,
            coreInstalled,
            coreTotal,
            declaredInstalled,
            declaredTotal,
            PatchInstaller.UnresolvedCount,
            PatchInstaller.FailedCount,
            OwnerRegistry.Count,
            HarmonyConflictRecorder.Count,
            GcObserverHost.IsRunning,
            TpsFpsObserverHost.IsRunning,
            AllocationSamplerHost.IsRunning,
            sessionId,
            CollectorRuntimeInfo.OwnerId);
    }
}
