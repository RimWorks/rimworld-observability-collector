using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorks.RimObs.Api;

namespace RimWorks.RimObs.Patching;

internal static class ObservedSectionScanner {
    public sealed class ScanResult {
        public int AssembliesScanned;
        public int AttributesFound;
        public int Registered;
        public int SkippedDuplicate = 0;
        public int SkippedUnsupported = 0;
        public int Failed = 0;
        public List<string> Warnings { get; } = new();
    }

    internal static bool? AttributesEnabledForTests { get; set; }

    private static bool IsEnabled() {
        if (AttributesEnabledForTests.HasValue)
            return AttributesEnabledForTests.Value;
        try {
            Type? settingsType = Type.GetType(
                "RimWorks.RimObs.Settings.RimObsSettings, RimObs");
            if (settingsType == null)
                return true;
            System.Reflection.PropertyInfo? currentProp =
                settingsType.GetProperty("Current",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
            object? instance = currentProp?.GetValue(null);
            if (instance == null)
                return true;
            System.Reflection.FieldInfo? field =
                settingsType.GetField("AttributesEnabled",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);
            if (field == null)
                return true;
            return (bool)field.GetValue(instance)!;
        }
        catch {
            return true;
        }
    }

    public static ScanResult Scan(IEnumerable<(string packageId, IReadOnlyList<Assembly> assemblies)> mods) {
        if (mods == null)
            throw new ArgumentNullException(nameof(mods));

        if (!IsEnabled())
            return new ScanResult();

        ScanResult result = new();
        foreach ((string packageId, IReadOnlyList<Assembly> assemblies) in mods) {
            if (string.IsNullOrEmpty(packageId) || assemblies == null)
                continue;

            for (int i = 0; i < assemblies.Count; i++)
                ScanAssembly(assemblies[i], packageId, result);
        }
        return result;
    }

    private static void ScanAssembly(Assembly assembly, string packageId, ScanResult result) {
        if (assembly == null)
            return;
        if (assembly.GetName().Name == "RimObs")
            return;

        result.AssembliesScanned++;
        if (!TryLoadTypes(assembly, packageId, result, out Type[] types))
            return;

        for (int i = 0; i < types.Length; i++)
            ScanType(types[i], packageId, result);
    }

    // A partly broken assembly still yields the types that did load, so salvage those and
    // record the loader errors rather than dropping the whole assembly.
    private static bool TryLoadTypes(Assembly assembly, string packageId, ScanResult result, out Type[] types) {
        try {
            types = assembly.GetTypes();
            return true;
        }
        catch (ReflectionTypeLoadException ex) {
            List<Type> salvaged = new();
            if (ex.Types != null) {
                foreach (Type? t in ex.Types) {
                    if (t != null)
                        salvaged.Add(t);
                }
            }
            if (ex.LoaderExceptions != null) {
                foreach (Exception? le in ex.LoaderExceptions) {
                    if (le != null)
                        result.Warnings.Add(
                            $"[{packageId}] {assembly.GetName().Name}: loader exception: {le.Message}");
                }
            }
            types = salvaged.ToArray();
            return true;
        }
        catch (Exception ex) {
            result.Failed++;
            result.Warnings.Add(
                $"[{packageId}] {assembly.GetName().Name}: GetTypes failed: {ex.Message}");
            types = Array.Empty<Type>();
            return false;
        }
    }

    private static void ScanType(Type type, string packageId, ScanResult result) {
        try {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly)) {
                try {
                    ScanMethod(method, type, packageId, result);
                }
                catch (Exception ex) {
                    result.Failed++;
                    result.Warnings.Add(
                        $"[{packageId}] {type.FullName}.{method.Name}: scan failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex) {
            result.Failed++;
            result.Warnings.Add(
                $"[{packageId}] {type.FullName}: type scan failed: {ex.Message}");
        }
    }

    private static void ScanMethod(MethodInfo method, Type type, string packageId, ScanResult result) {
        ObservedSectionAttribute? attr = method.GetCustomAttribute<ObservedSectionAttribute>();
        if (attr == null)
            return;

        result.AttributesFound++;

        if (!IsPatchable(method, out string reason)) {
            result.SkippedUnsupported++;
            result.Warnings.Add(
                $"[{packageId}] {type.FullName}.{method.Name}: skipped ({reason})");
            return;
        }

        if (SectionCatalog.TryGetSectionId(method, out _)) {
            result.SkippedDuplicate++;
            result.Warnings.Add(
                $"[{packageId}] {type.FullName}.{method.Name}: skipped (already registered by core or XML)");
            return;
        }

        string computedName = attr.Name ?? $"{type.FullName}.{method.Name}";
        string prefixed = $"{packageId}.{computedName}";
        SectionCatalog.RegisterDirect(prefixed, method, subsystem: attr.Subsystem);
        PatchInstaller.PatchAttributeMethod(method);
        result.Registered++;
    }

    private static bool IsPatchable(MethodInfo method, out string reason) {
        if (method.IsAbstract) {
            reason = "method is abstract";
            return false;
        }
        if (method.ContainsGenericParameters) {
            reason = "method has unresolved generic parameters";
            return false;
        }
        if (method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null) {
            reason = "async methods are not supported in v1";
            return false;
        }
        if (method.GetCustomAttribute<System.Runtime.CompilerServices.IteratorStateMachineAttribute>() != null) {
            reason = "iterator (yield) methods are not supported in v1";
            return false;
        }
        reason = string.Empty;
        return true;
    }
}
