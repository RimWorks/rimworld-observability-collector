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

        return new CollectorStatus {
            CollectorRunning = CollectorRuntimeInfo.CollectorRunning,
            LaunchAttempted = CollectorRuntimeInfo.LaunchAttempted,
            Host = CollectorRuntimeInfo.Host,
            Port = CollectorRuntimeInfo.Port,
            ControlPort = controlPort,
            ProfilerEnabled = Profiler.Enabled,
            CoreInstalled = coreInstalled,
            CoreTotal = coreTotal,
            DeclaredInstalled = declaredInstalled,
            DeclaredTotal = declaredTotal,
            UnresolvedCount = PatchInstaller.UnresolvedCount,
            FailedCount = PatchInstaller.FailedCount,
            OwnerCount = OwnerRegistry.Count,
            ConflictCount = PatchConflictRecorder.Count,
            GcObserverRunning = GcObserverHost.IsRunning,
            TpsFpsObserverRunning = TpsFpsObserverHost.IsRunning,
            AllocationSamplerRunning = AllocationSamplerHost.IsRunning,
            SessionId = sessionId,
            OwnerId = CollectorRuntimeInfo.OwnerId,
        };
    }
}
