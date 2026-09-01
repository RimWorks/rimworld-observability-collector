using System.Reflection;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Observers;

namespace RimWorks.RimObs.Patching;

// Zero-arg prefixes and postfixes on the game's tick and frame entry points, so TpsFpsObserver
// can derive live TPS/FPS by differencing call counts.
internal static class FrameTickPatches {
    internal const string TickSection = "Verse.TickManager.DoSingleTick";
    internal const string FrameSection = "Verse.Root_Play.Update";

    public static int InstalledCount { get; private set; }

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
            backend.PatchPostfix(frame, Own(nameof(FramePostfix)));
            InstalledCount++;
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

    private static void FramePostfix() => FrameTickCounters.RecordFrame();
}
