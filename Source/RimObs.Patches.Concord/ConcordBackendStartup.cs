using RimWorks.RimObs.Patching;
using Verse;

namespace RimWorks.RimObs.Patches.Concord;

// registration lives here, not on ConcordBackend, so the backend stays Verse-free and the
// net10 test project can compile it.
[StaticConstructorOnStartup]
public static class ConcordBackendStartup {
    static ConcordBackendStartup() {
        PatchBackends.Register(new ConcordBackend(), PatchBackends.ConcordPriority);
    }
}
