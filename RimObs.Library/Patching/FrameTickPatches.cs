using System.Reflection;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Observers;
using HarmonyLib;

namespace RimWorks.RimObs.Patching;

// Installs zero-arg Harmony postfixes on the game's per-tick and per-frame entry points so the
// TpsFpsObserver can derive live TPS/FPS by differencing call counts. These compose with the
// timing transpiler already applied to the same methods (Harmony chains postfix + transpiler).
internal static class FrameTickPatches {
    public static int InstalledCount { get; private set; }

    public static void InstallAll() {
        Harmony harmony = PatchInstaller.EnsureHarmony(PatchInstaller.HarmonyId);

        TryPatch(harmony, "Verse.TickManager:DoSingleTick", nameof(TickPostfix));
        TryPatch(harmony, "Verse.Root_Play:Update", nameof(FramePostfix));
        TryPatchPrefix(harmony, "Verse.TickManager:DoSingleTick", nameof(DrainControlOpsPrefix));
    }

    private static void TryPatch(Harmony harmony, string targetName, string postfixName) {
        MethodBase? target = AccessTools.Method(targetName);
        if (target == null)
            return;

        MethodInfo postfix = AccessTools.Method(typeof(FrameTickPatches), postfixName);
        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        InstalledCount++;
    }

    private static void TryPatchPrefix(Harmony harmony, string targetName, string prefixName) {
        MethodBase? target = AccessTools.Method(targetName);
        if (target == null)
            return;

        MethodInfo prefix = AccessTools.Method(typeof(FrameTickPatches), prefixName);
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        InstalledCount++;
    }

    private static void DrainControlOpsPrefix() => ControlServices.Queue.Drain();

    private static void TickPostfix() => FrameTickCounters.RecordTick();

    private static void FramePostfix() => FrameTickCounters.RecordFrame();
}
