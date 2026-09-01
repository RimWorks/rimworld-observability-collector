using RimWorks.RimObs.Patches.Harmony;
using RimWorks.RimObs.Patching;

namespace RimWorks.RimObs.Tests;

// tests that install patches need a live backend. the real one registers from a static
// constructor RimWorld runs and xunit does not.
internal static class TestBackend {
    public static void Activate() {
        PatchBackends.ResetForTests();
        PatchBackends.Register(new HarmonyBackend(), PatchBackends.HarmonyPriority);
        PatchBackends.SelectBest(scan: false);
    }

    public static void Deactivate() {
        PatchBackends.Active?.UnpatchAllForTests();
        PatchBackends.ResetForTests();
    }
}
