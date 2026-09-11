using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Patching;

namespace RimWorks.RimObs.Auto;

/// <summary>
/// Walks loaded assemblies once and reports how many methods a filter list matches, how many
/// survive the triviality filter, and which ones to patch.
/// </summary>
internal static class AutoInstrumentScanner {
    public const int DefaultMaxTargets = 8192;

    private const BindingFlags MethodFlags =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    public static AutoInstrumentPlan Scan(
        IEnumerable<Assembly> assemblies,
        MethodPattern[] patterns,
        int maxTargets = DefaultMaxTargets
    ) {
        AutoInstrumentPlan plan = new AutoInstrumentPlan { MaxTargets = maxTargets };
        MethodPattern.Split(patterns, out MethodPattern[] includes, out MethodPattern[] excludes);
        if (includes.Length == 0)
            return plan;

        foreach (Assembly assembly in assemblies) {
            string name = assembly.GetName().Name ?? string.Empty;
            if (!MethodPattern.AnyAssembly(includes, name))
                continue;
            ScanAssembly(assembly, name, includes, excludes, plan, maxTargets);
        }

        return plan;
    }

    private static void ScanAssembly(
        Assembly assembly, string assemblyName, MethodPattern[] patterns, MethodPattern[] excludes,
        AutoInstrumentPlan plan, int maxTargets
    ) {
        foreach (Type type in SafeTypes(assembly)) {
            string? typeName = type.FullName;
            if (typeName is null || type.ContainsGenericParameters)
                continue;
            if (!AnyTypeMatch(patterns, assemblyName, typeName))
                continue;

            ScanType(type, typeName, assemblyName, patterns, excludes, plan, maxTargets);
        }
    }

    private static void ScanType(
        Type type, string typeName, string assemblyName, MethodPattern[] patterns, MethodPattern[] excludes,
        AutoInstrumentPlan plan, int maxTargets
    ) {
        MethodInfo[] methods;
        try {
            methods = type.GetMethods(MethodFlags);
        }
        catch (Exception) {
            return;
        }

        for (int i = 0; i < methods.Length; i++) {
            MethodInfo method = methods[i];
            if (!AnyMatch(patterns, assemblyName, typeName, method.Name))
                continue;
            // an exclusion wins wherever it sits in the list, so it is checked after the include.
            if (AnyMatch(excludes, assemblyName, typeName, method.Name)) {
                plan.SkippedIgnored++;
                continue;
            }

            plan.Matched++;
            Classify(method, typeName, plan, maxTargets);
        }
    }

    private static void Classify(MethodInfo method, string typeName, AutoInstrumentPlan plan, int maxTargets) {
        if (MethodResolver.IsBlocklisted(typeName) || MethodResolver.HasUnmanageableThis(method)) {
            plan.SkippedBlocklisted++;
            return;
        }

        if (TrivialMethodFilter.IsTrivial(method)) {
            plan.SkippedTrivial++;
            return;
        }

        // a core-pack, declared or attribute section already owns this method. patching it a
        // second time double-counts under Harmony and overwrites the handle under Concord.
        if (SectionCatalog.TryGetSectionId(method, out int _)) {
            plan.SkippedAlreadyInstrumented++;
            return;
        }

        if (plan.Targets.Count >= maxTargets) {
            plan.SkippedOverCap++;
            return;
        }

        plan.Targets.Add(method);
    }

    private static bool AnyTypeMatch(MethodPattern[] patterns, string assemblyName, string typeName) {
        for (int i = 0; i < patterns.Length; i++) {
            if (patterns[i].MatchesAssembly(assemblyName) && patterns[i].MatchesType(typeName))
                return true;
        }
        return false;
    }

    private static bool AnyMatch(MethodPattern[] patterns, string assemblyName, string typeName, string methodName) {
        for (int i = 0; i < patterns.Length; i++) {
            MethodPattern pattern = patterns[i];
            if (pattern.MatchesAssembly(assemblyName)
                && pattern.MatchesType(typeName)
                && pattern.MatchesMethod(methodName))
                return true;
        }
        return false;
    }

    private static Type[] SafeTypes(Assembly assembly) {
        try {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex) {
            return Salvage(ex);
        }
        catch (Exception) {
            return Array.Empty<Type>();
        }
    }

    private static Type[] Salvage(ReflectionTypeLoadException ex) {
        if (ex.Types is null)
            return Array.Empty<Type>();

        List<Type> kept = new List<Type>(ex.Types.Length);
        foreach (Type? type in ex.Types) {
            if (type is not null)
                kept.Add(type);
        }
        return kept.ToArray();
    }
}
