using System;
using System.Collections.Generic;
using System.Reflection;

using RimWorks.RimObs.Logging;
using Log = RimWorks.RimLogging.Log;

namespace RimWorks.RimObs.Patching;

/// <summary>Picks one backend and holds it. Highest priority wins, so Concord beats Harmony.</summary>
public static class PatchBackends {
    public const int ConcordPriority = 100;
    public const int HarmonyPriority = 0;

    private static readonly List<KeyValuePair<IPatchBackend, int>> s_Registered = new();

    public static IPatchBackend? Active { get; private set; }

    /// <summary>Priority the winner won at. This type cannot log, so the caller reports it.</summary>
    public static int ActivePriority { get; private set; }

    public static void Register(IPatchBackend backend, int priority) {
        s_Registered.Add(new KeyValuePair<IPatchBackend, int>(backend, priority));
    }

    /// <summary>Runs once at bootstrap. Leaves Active null so the mod degrades instead of crashing.</summary>
    public static void SelectBest() => SelectBest(scan: true);

    internal static void SelectBest(bool scan) {
        if (Active != null)
            return;

        if (scan)
            ScanForBackends();

        IPatchBackend? best = null;
        int bestPriority = int.MinValue;
        for (int i = 0; i < s_Registered.Count; i++) {
            if (s_Registered[i].Value > bestPriority) {
                bestPriority = s_Registered[i].Value;
                best = s_Registered[i].Key;
            }
        }

        Active = best;
        ActivePriority = best == null ? 0 : bestPriority;

        // the caller owns the no-backend case: it also raises the player-facing dialog.
        if (best != null) {
            Log.InfoTo(
                LogChannels.Patching,
                "patching via {Backend} at priority {Priority}, {Candidates} candidate(s) seen",
                new object?[] { best.Name, bestPriority, s_Registered.Count }
            );
        }
    }

    // CallAll runs after our install is queued, so the backends have not registered themselves
    // yet. Find them directly. See Verse.PlayDataLoader.DoPlayLoad.
    private static void ScanForBackends() {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
            ScanAssembly(assemblies[i]);
    }

    private static void ScanAssembly(Assembly assembly) {
        Type[] types;
        try {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex) {
            types = SalvageTypes(ex);
        }
        catch (Exception) {
            return;
        }

        {
            for (int t = 0; t < types.Length; t++) {
                // IsAssignableFrom walks the interface map, so a salvaged type whose interfaces
                // live in a missing assembly throws here. one broken mod must not stop the scan.
                try {
                    Type type = types[t];
                    if (type.IsAbstract || type.IsInterface || !typeof(IPatchBackend).IsAssignableFrom(type))
                        continue;
                    if (AlreadyRegistered(type))
                        continue;

                    if (Activator.CreateInstance(type) is IPatchBackend backend)
                        Register(backend, PriorityOf(type));
                }
                catch (Exception ex) {
                    // a type we cannot inspect or construct is not a backend. try the next one.
                    // trace, not warn: a half-loaded assembly can hit this for hundreds of types.
                    Log.TraceTo(
                        LogChannels.Patching,
                        "backend scan skipped {Type}: {Reason}",
                        new object?[] { types[t].FullName, ex.Message }
                    );
                }
            }
        }
    }

    // the scan also runs when a backend registered itself, so do not add a second of its type.
    private static bool AlreadyRegistered(Type type) {
        for (int i = 0; i < s_Registered.Count; i++) {
            if (s_Registered[i].Key.GetType() == type)
                return true;
        }
        return false;
    }

    // a half-loaded assembly must not take the whole scan down.
    private static Type[] SalvageTypes(ReflectionTypeLoadException ex) {
        if (ex.Types == null)
            return Array.Empty<Type>();

        List<Type> salvaged = new(ex.Types.Length);
        foreach (Type? type in ex.Types) {
            if (type != null)
                salvaged.Add(type);
        }
        return salvaged.ToArray();
    }

    // the backend's own static constructor has not run, so it cannot have told us its priority.
    private static int PriorityOf(Type type) =>
        type.Name.IndexOf("Concord", StringComparison.OrdinalIgnoreCase) >= 0
            ? ConcordPriority
            : HarmonyPriority;

    internal static void ResetForTests() {
        s_Registered.Clear();
        Active = null;
        ActivePriority = 0;
    }
}
