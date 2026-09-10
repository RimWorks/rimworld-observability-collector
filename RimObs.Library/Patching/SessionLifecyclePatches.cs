using System;
using System.Reflection;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Session;

namespace RimWorks.RimObs.Patching;

// A session is one launch of the game, which makes a long play one undivided run. Entering a
// game is the natural seam, so loading a save, starting a colony or a quickstart each begin a
// fresh session. Backend-agnostic: IPatchBackend covers Harmony and Concord alike.
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
