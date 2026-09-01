using RimWorks.RimObs.Patching;
using Verse;

namespace RimWorks.RimObs.Patches.Harmony;

// registration lives here, not on HarmonyBackend, so the backend stays Verse-free and the
// net10 test project can compile it.
[StaticConstructorOnStartup]
public static class HarmonyBackendStartup {
    static HarmonyBackendStartup() {
        PatchBackends.Register(new HarmonyBackend(), PatchBackends.HarmonyPriority);
    }
}
