using System;
using System.Collections.Generic;
using System.Reflection;

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

        // no Log call: stays Verse-free so this compiles into the net10 test project.
        Active = best;
        ActivePriority = best == null ? 0 : bestPriority;
    }

    // CallAll runs after our install is queued, so the backends have not registered themselves
    // yet. Find them directly. See Verse.PlayDataLoader.DoPlayLoad.
    private static void ScanForBackends() {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++) {
            Type[] types;
            try {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex) {
                types = SalvageTypes(ex);
            }
            catch (Exception) {
                continue;
            }

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
                catch (Exception) {
                    // a type we cannot inspect or construct is not a backend. try the next one.
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
