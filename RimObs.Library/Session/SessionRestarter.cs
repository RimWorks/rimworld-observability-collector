using System;
using System.Reflection;
using RimWorks.RimObs.Metrics;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Transport;

namespace RimWorks.RimObs.Session;

/// <summary>
/// Starts a fresh session inside a running game. A session is normally one launch, so this is
/// the only way to split a long play into runs you can compare.
/// </summary>
public static class SessionRestarter {
    private static UdpTelemetrySink? s_Sink;
    private static readonly object s_Lock = new object();

    internal static void SetSink(UdpTelemetrySink? sink) => s_Sink = sink;

    /// <summary>
    /// Restarts the game process. A session is one launch, so a restart is the only split that
    /// needs no re-anchoring: the new process mints its own id on the way up. Optionally saves
    /// first, because Root.Shutdown does not, and unsaved colony progress is lost.
    /// Must run on the game thread: it touches the window stack and the save pipeline.
    /// </summary>
    public static void RestartGame(bool save) {
        if (save)
            TrySaveCurrentGame();
        InvokeStatic("Verse.GenCommandLine, Assembly-CSharp", "Restart");
    }

    private static void TrySaveCurrentGame() {
        Type? loader = Type.GetType("Verse.GameDataSaveLoader, Assembly-CSharp", throwOnError: false);
        if (loader == null)
            return;

        MethodInfo? save = loader.GetMethod("SaveGame", BindingFlags.Public | BindingFlags.Static);
        if (save == null)
            return;

        // no game loaded means nothing to save, and SaveGame would throw on the null world.
        string? name = CurrentSaveName();
        if (name == null)
            return;

        save.Invoke(null, new object?[] { name });
    }

    /// <summary>The name of the save the colony is already using, or null at the main menu.</summary>
    private static string? CurrentSaveName() {
        Type? current = Type.GetType("Verse.Current, Assembly-CSharp", throwOnError: false);
        object? game = current?.GetProperty("Game", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (game == null)
            return null;

        object? info = game.GetType().GetProperty("Info", BindingFlags.Public | BindingFlags.Instance)?.GetValue(game);
        string? name = info?.GetType()
            .GetProperty("permadeathModeUniqueName", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(info) as string;
        return string.IsNullOrEmpty(name) ? "RimObs-autosave" : name;
    }

    private static void InvokeStatic(string typeName, string methodName) {
        Type? type = Type.GetType(typeName, throwOnError: false);
        type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
    }

    /// <summary>
    /// Re-anchors onto a new id and re-queues every registration, so the collector's name table
    /// refills for the new session instead of leaving every section as a bare id. Returns the
    /// new session id. Locked because the entry hook and the dashboard button can both call it.
    /// </summary>
    public static string StartNew() {
        lock (s_Lock) {
            string id = Guid.NewGuid().ToString("N");
            SessionAnchor.Restart(id);
            SectionRegistry.RequeueAllRegistrations();
            MetricRegistry.RequeueAllRegistrations();
            s_Sink?.RestartMetaBurst();
            return id;
        }
    }
}
