using System;

namespace RimWorks.RimObs.Bootstrap;

/// <summary>
/// Hands the instrumentation install to a scheduler instead of running it inline.
/// </summary>
public static class InstrumentationInstall {
    private static bool s_Scheduled;

    /// <summary>
    /// Queues <paramref name="install"/> on <paramref name="scheduler"/>, at most once.
    /// </summary>
    // Patching resolves each target's MethodBase, which runs the declaring type's static
    // constructor. RimWorld's UI types build materials there, and Unity refuses that off the
    // main thread.
    public static void Schedule(Action<Action> scheduler, Action install) {
        if (s_Scheduled)
            return;
        s_Scheduled = true;
        scheduler(install);
    }

    internal static void ResetForTests() => s_Scheduled = false;
}
