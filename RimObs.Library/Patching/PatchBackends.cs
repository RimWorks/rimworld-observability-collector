using System.Collections.Generic;

namespace RimWorks.RimObs.Patching;

/// <summary>
/// Picks one backend and holds it. Highest priority wins, so Concord beats Harmony.
/// </summary>
public static class PatchBackends {
    public const int ConcordPriority = 100;
    public const int HarmonyPriority = 0;

    private static readonly List<KeyValuePair<IPatchBackend, int>> s_Registered = new();

    public static IPatchBackend? Active { get; private set; }

    public static void Register(IPatchBackend backend, int priority) {
        s_Registered.Add(new KeyValuePair<IPatchBackend, int>(backend, priority));
    }

    /// <summary>Runs once at bootstrap. Leaves Active null so the mod degrades instead of crashing.</summary>
    public static void SelectBest() {
        if (Active != null)
            return;

        IPatchBackend? best = null;
        int bestPriority = int.MinValue;
        for (int i = 0; i < s_Registered.Count; i++) {
            if (s_Registered[i].Value > bestPriority) {
                bestPriority = s_Registered[i].Value;
                best = s_Registered[i].Key;
            }
        }

        // no Log call: stays Verse-free so this compiles into the net10 test project.
        Active = best;
    }

    internal static void ResetForTests() {
        s_Registered.Clear();
        Active = null;
    }
}
