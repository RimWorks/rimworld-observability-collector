using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorks.RimObs.Profile;

namespace RimWorks.RimObs.Patching;

internal static class SectionCatalog {
    private static readonly List<CatalogEntry> s_Entries = new();
    private static readonly Dictionary<MethodBase, int> s_MethodToSectionId = new();
    private static readonly object s_Lock = new();
    private static bool s_CorePackRegistered;

    public static IReadOnlyList<CatalogEntry> Entries {
        get {
            lock (s_Lock) {
                return s_Entries.ToArray();
            }
        }
    }

    private static readonly (string Name, string TypeName, string MethodName)[] s_CorePackSections =
    [
        (FrameTickPatches.TickSection, "Verse.TickManager", "DoSingleTick"),
        ("Verse.Map.MapPreTick", "Verse.Map", "MapPreTick"),
        ("Verse.Map.MapPostTick", "Verse.Map", "MapPostTick"),
        ("RimWorld.Planet.World.WorldTick", "RimWorld.Planet.World", "WorldTick"),
        ("Verse.GameComponentUtility.GameComponentTick", "Verse.GameComponentUtility", "GameComponentTick"),
        ("RimWorld.Storyteller.StorytellerTick", "RimWorld.Storyteller", "StorytellerTick"),
        ("RimWorld.History.HistoryTick", "RimWorld.History", "HistoryTick"),
        ("RimWorld.FilthMonitor.FilthMonitorTick", "RimWorld.FilthMonitor", "FilthMonitorTick"),
        ("RimWorld.DateNotifier.DateNotifierTick", "RimWorld.DateNotifier", "DateNotifierTick"),
        ("Verse.AI.Pawn_JobTracker.DetermineNextJob", "Verse.AI.Pawn_JobTracker", "DetermineNextJob"),
        // pawn movement goes through PushRequest and lands here once per map tick. FindPathNow
        // only serves the rare synchronous callers, so it stays as a separate .sync section.
        ("Verse.PathFinder.PathFinderTick", "Verse.PathFinder", "PathFinderTick"),
        ("Verse.PathFinder.FindPathNow.sync", "Verse.PathFinder", "FindPathNow"),
        ("Verse.AI.Pawn_PathFollower.PatherTick", "Verse.AI.Pawn_PathFollower", "PatherTick"),
        ("RimWorld.MapInterface.MapInterfaceUpdate", "RimWorld.MapInterface", "MapInterfaceUpdate"),
        ("Verse.UIRoot.UIRootOnGUI", "Verse.UIRoot", "UIRootOnGUI"),
        ("Verse.WindowStack.WindowStackOnGUI", "Verse.WindowStack", "WindowStackOnGUI"),
        ("RimWorld.Alert.Recalculate", "RimWorld.Alert", "Recalculate"),
        ("RimWorld.Pawn_NeedsTracker.NeedsTrackerTickInterval", "RimWorld.Pawn_NeedsTracker", "NeedsTrackerTickInterval"),
        ("RimWorld.Planet.WorldPawns.WorldPawnsTick", "RimWorld.Planet.WorldPawns", "WorldPawnsTick"),
        (FrameTickPatches.FrameSection, "Verse.Root_Play", "Update"),
    ];

    public static void RegisterCorePack() {
        lock (s_Lock) {
            if (s_CorePackRegistered)
                return;
            s_CorePackRegistered = true;

            for (int i = 0; i < s_CorePackSections.Length; i++) {
                (string name, string typeName, string methodName) = s_CorePackSections[i];
                Register(name, typeName, methodName, null);
            }
        }
    }

    public static CatalogEntry Register(string name, string typeName, string methodName, string[]? paramTypeNames, string? subsystem = null) {
        lock (s_Lock) {
            CatalogEntry entry = new(name, typeName, methodName, paramTypeNames);
            entry.Subsystem = subsystem;
            s_Entries.Add(entry);
            return entry;
        }
    }

    public static CatalogEntry RegisterDirect(string name, MethodBase method, string? subsystem = null) {
        if (method == null)
            throw new ArgumentNullException(nameof(method));

        lock (s_Lock) {
            CatalogEntry entry = new(name, method.DeclaringType?.FullName ?? "?", method.Name, null);
            entry.Resolved = method;
            entry.Subsystem = subsystem;
            SectionHandle handle = SectionRegistry.Register(name, subsystem);
            entry.SectionId = handle.Id;
            SectionRegistry.SetActive(handle.Id, true);
            s_Entries.Add(entry);
            MapMethod(method, handle.Id);
            return entry;
        }
    }

    public static void ResolveAll() {
        lock (s_Lock) {
            foreach (CatalogEntry entry in s_Entries) {
                if (entry.Resolved != null)
                    continue;

                try {
                    Type? type = FindType(entry.TypeName);
                    if (type == null) {
                        entry.ResolutionError = new TypeLoadException($"type '{entry.TypeName}' not found");
                        continue;
                    }

                    MethodInfo? method = ResolveMethod(type, entry.MethodName, entry.ParamTypeNames);
                    if (method == null) {
                        entry.ResolutionError = new MissingMethodException(entry.TypeName, entry.MethodName);
                        continue;
                    }

                    SectionHandle handle = SectionRegistry.Register(entry.Name, entry.Subsystem);
                    entry.SectionId = handle.Id;
                    entry.Resolved = method;
                    SectionRegistry.SetActive(handle.Id, true);
                    MapMethod(method, handle.Id);
                }
                catch (Exception ex) {
                    entry.ResolutionError = ex;
                }
            }
        }
    }

    // an async or iterator target is patched through its generated MoveNext, so map that too.
    // whichever method the backend reports as the original then resolves to the same section.
    private static void MapMethod(MethodBase method, int sectionId) {
        s_MethodToSectionId[method] = sectionId;
        if (StateMachineTarget.TryResolveMoveNext(method, out MethodInfo? moveNext) && moveNext != null)
            s_MethodToSectionId[moveNext] = sectionId;
    }

    public static bool TryGetSectionId(MethodBase method, out int sectionId) {
        lock (s_Lock) {
            return s_MethodToSectionId.TryGetValue(method, out sectionId);
        }
    }

    public static void Clear() {
        lock (s_Lock) {
            s_Entries.Clear();
            s_MethodToSectionId.Clear();
            s_CorePackRegistered = false;
        }
    }

    private static Type? FindType(string fullName) {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
            Type? t = asm.GetType(fullName, throwOnError: false);
            if (t != null)
                return t;
        }
        return null;
    }

    private static MethodInfo? ResolveMethod(Type type, string methodName, string[]? paramTypeNames) {
        List<MethodInfo> byName = GetMethodsByName(type, methodName);
        if (byName.Count == 0)
            return null;

        if (paramTypeNames == null || paramTypeNames.Length == 0)
            return ResolveByNameOnly(byName);

        return ResolveByParamTypes(byName, paramTypeNames);
    }

    private static List<MethodInfo> GetMethodsByName(Type type, string methodName) {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        MethodInfo[] candidates = type.GetMethods(Flags);
        List<MethodInfo> byName = new();
        for (int i = 0; i < candidates.Length; i++) {
            if (candidates[i].Name == methodName)
                byName.Add(candidates[i]);
        }
        return byName;
    }

    // name-only entries take the widest overload: vanilla's lower-arity forms are usually thin
    // wrappers that delegate to it, so patching the wide one covers both call paths.
    private static MethodInfo? ResolveByNameOnly(List<MethodInfo> byName) {
        if (byName.Count == 1)
            return byName[0];

        MethodInfo best = byName[0];
        int bestArity = best.GetParameters().Length;
        for (int i = 1; i < byName.Count; i++) {
            int arity = byName[i].GetParameters().Length;
            if (arity > bestArity) {
                best = byName[i];
                bestArity = arity;
            }
        }
        return best;
    }

    private static MethodInfo? ResolveByParamTypes(List<MethodInfo> byName, string[] paramTypeNames) {
        foreach (MethodInfo m in byName) {
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length != paramTypeNames.Length)
                continue;

            bool match = true;
            for (int i = 0; i < ps.Length; i++) {
                if (!NameMatches(ps[i].ParameterType, paramTypeNames[i])) {
                    match = false;
                    break;
                }
            }
            if (match)
                return m;
        }
        return null;
    }

    private static bool NameMatches(Type t, string name) {
        if (t.FullName == name)
            return true;
        if (t.Name == name)
            return true;
        if (t.IsGenericType) {
            string definition = t.GetGenericTypeDefinition().FullName ?? "";
            if (definition == name)
                return true;
        }
        return false;
    }
}
