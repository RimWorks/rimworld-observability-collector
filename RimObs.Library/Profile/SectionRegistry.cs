using System;
using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Profile;

internal static class SectionRegistry {
    public const int MaxSections = 4096;

    internal static readonly string[] s_Names = new string[MaxSections];
    internal static readonly bool[] s_Active = new bool[MaxSections];
    internal static readonly string?[] s_Subsystems = new string?[MaxSections];

    private static readonly Dictionary<string, int> s_Lookup = new(StringComparer.Ordinal);
    private static readonly object s_Lock = new();
    private static int s_Count;

    private static readonly List<int> s_PendingRegistrations = [];

    public static int Count {
        get {
            lock (s_Lock) {
                return s_Count;
            }
        }
    }

    public static SectionHandle Register(string name, string? subsystem = null) {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Section name must not be empty.", nameof(name));

        lock (s_Lock) {
            if (s_Lookup.TryGetValue(name, out int existing))
                return new SectionHandle(existing);

            if (s_Count >= MaxSections)
                throw new InvalidOperationException(
                    $"Section registry full (max {MaxSections}). Section '{name}' could not be registered."
                );

            int id = s_Count++;
            s_Names[id] = name;
            s_Active[id] = true;
            s_Subsystems[id] = subsystem;
            s_Lookup[name] = id;
            s_PendingRegistrations.Add(id);
            return new SectionHandle(id);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsActive(int id) => (uint)id < (uint)s_Count && s_Active[id];

    public static string GetName(int id) =>
        (uint)id < (uint)s_Count ? s_Names[id] : string.Empty;

    public static string? GetSubsystem(int id) =>
        (uint)id < (uint)s_Count ? s_Subsystems[id] : null;

    public static void SetActive(int id, bool active) {
        if ((uint)id < (uint)s_Count)
            s_Active[id] = active;
    }

    public static void ApplyDisabledSet(HashSet<string> disabled) {
        if (disabled == null)
            throw new ArgumentNullException(nameof(disabled));

        lock (s_Lock) {
            for (int id = 0; id < s_Count; id++)
                s_Active[id] = !disabled.Contains(s_Names[id]);
        }
    }

    public static int DrainPendingRegistrations(int[] ids, string[] names, string?[] subsystems) {
        lock (s_Lock) {
            int n = Math.Min(s_PendingRegistrations.Count, Math.Min(ids.Length, Math.Min(names.Length, subsystems.Length)));
            for (int i = 0; i < n; i++) {
                int id = s_PendingRegistrations[i];
                ids[i] = id;
                names[i] = s_Names[id];
                subsystems[i] = s_Subsystems[id];
            }
            s_PendingRegistrations.RemoveRange(0, n);
            return n;
        }
    }

    public static void Clear() {
        lock (s_Lock) {
            Array.Clear(s_Names, 0, s_Count);
            Array.Clear(s_Active, 0, s_Count);
            Array.Clear(s_Subsystems, 0, s_Count);
            s_Lookup.Clear();
            s_PendingRegistrations.Clear();
            s_Count = 0;
        }
    }
}
