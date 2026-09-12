using System;
using System.Reflection;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Session;

namespace RimWorks.RimObs.Patching;

// entering a game is the session seam: loading a save, starting a colony, or a quickstart
// each begin a fresh session. backend-agnostic via IPatchBackend.
internal static class SessionLifecyclePatches {
    internal const string EntryMethod = "Verse.Game.FinalizeInit";

    public static int InstalledCount { get; private set; }

    public static void InstallAll() {
        IPatchBackend? backend = PatchBackends.Active;
        if (backend == null)
            return;

        MethodBase? entry = ResolveEntry();
        if (entry == null)
            return;

        backend.PatchPostfix(entry, Own(nameof(GameEnteredPostfix)));
        InstalledCount++;
    }

    private static MethodBase? ResolveEntry() {
        Type? game = Type.GetType("Verse.Game, Assembly-CSharp", throwOnError: false);
        return game?.GetMethod("FinalizeInit", BindingFlags.Public | BindingFlags.Instance);
    }

    private static MethodInfo Own(string name) =>
        typeof(SessionLifecyclePatches).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    // Splitting an empty session just leaves an empty one behind, so the first entry from the
    // main menu keeps the session the mod started with.
    private static void GameEnteredPostfix() {
        if (FrameTickCounters.Ticks == 0)
            return;
        SessionRestarter.StartNew();
    }
}
