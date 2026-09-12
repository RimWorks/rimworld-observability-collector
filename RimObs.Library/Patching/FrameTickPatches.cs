using System;
using System.Reflection;
using RimWorks.RimObs.Auto;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Transport;

namespace RimWorks.RimObs.Patching;

// Zero-arg prefixes and postfixes on the game's tick and frame entry points, so TpsFpsObserver
// can derive live TPS/FPS by differencing call counts.
internal static class FrameTickPatches {
    internal const string TickSection = "Verse.TickManager.DoSingleTick";
    internal const string FrameSection = "Verse.Root_Play.Update";

    public static int InstalledCount { get; private set; }

    /// <summary>Set by the bootstrap; true once the game is genuinely playing a map.</summary>
    public static Func<bool>? AllocArmGate { get; set; }

    public static void InstallAll() {
        IPatchBackend? backend = PatchBackends.Active;
        if (backend == null)
            return;

        MethodBase? tick = ResolvedSection(TickSection);
        if (tick != null) {
            backend.PatchPostfix(tick, Own(nameof(TickPostfix)));
            backend.PatchPrefix(tick, Own(nameof(DrainControlOpsPrefix)));
            InstalledCount += 2;
        }

        MethodBase? frame = ResolvedSection(FrameSection);
        if (frame != null) {
            backend.PatchPrefix(frame, Own(nameof(FrameBeginPrefix)));
            backend.PatchPostfix(frame, Own(nameof(FramePostfix)));
            InstalledCount += 2;
        }
    }

    // the core pack resolved both of these already, so reuse that instead of a second lookup.
    private static MethodBase? ResolvedSection(string sectionName) {
        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (entry.Name == sectionName)
                return entry.Resolved;
        }
        return null;
    }

    private static MethodInfo Own(string name) =>
        typeof(FrameTickPatches).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void DrainControlOpsPrefix() => ControlServices.Queue.Drain();

    private static void TickPostfix() => FrameTickCounters.RecordTick();

    // frames keep rendering while the colony is paused, ticks dont, so this is the drain site
    // that lets a patch request land on a paused game.
    internal static void FrameBeginPrefix() {
        MainThreadMarker.Mark();
        // the gate is verse-side (ProgramState.Playing): ticks fire during map generation, so
        // a tick count can arm the hook inside the worldgen JIT storm it must wait out.
        if (AllocationHook.DeferredPending
            && AutoInstrumentRunner.Pending == 0
            && AllocArmGate?.Invoke() == true) {
            AllocationHook.TryEnableDeferred();
        }
        Profile.Profiler.HealPinnedAtFrameBoundary();
        FrameTickCounters.BeginFrame();
        ControlServices.Queue.Drain();
        AutoInstrumentRunner.Pump();
    }

    private static void FramePostfix() {
        FrameTickCounters.RecordFrame();
        FrameTickCounters.NoteCollections(GC.CollectionCount(0));
    }
}
