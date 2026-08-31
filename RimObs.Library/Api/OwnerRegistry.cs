using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace RimWorks.RimObs.Api;

public static class OwnerRegistry {
    private static readonly Dictionary<Assembly, string> s_AssemblyToPackageId = new();
    private static readonly object s_Lock = new();
    private static Func<Assembly, string?>? s_LateResolver;

    /// <summary>
    /// Installs a fallback resolver used by <see cref="TryGetPackageId"/> on cache miss.
    /// RimObsMod wires this to a Verse-aware scan of LoadedModManager so consumer mods whose
    /// Mod ctor runs before RimObsMod still resolve. Pass null to clear (used by tests).
    /// </summary>
    public static void SetLateResolver(Func<Assembly, string?>? resolver) {
        lock (s_Lock) {
            s_LateResolver = resolver;
        }
    }

    public static void RegisterMod(Assembly assembly, string packageId) {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrEmpty(packageId))
            throw new ArgumentException("packageId must not be empty.", nameof(packageId));

        lock (s_Lock) {
            s_AssemblyToPackageId[assembly] = packageId;
        }
    }

    public static bool TryGetPackageId(Assembly assembly, [MaybeNullWhen(false)] out string packageId) {
        Func<Assembly, string?>? resolver;
        lock (s_Lock) {
            if (s_AssemblyToPackageId.TryGetValue(assembly, out packageId))
                return true;
            resolver = s_LateResolver;
        }

        if (resolver != null) {
            string? resolved = resolver(assembly);
            if (!string.IsNullOrEmpty(resolved)) {
                lock (s_Lock) {
                    s_AssemblyToPackageId[assembly] = resolved!;
                }
                packageId = resolved!;
                return true;
            }
        }

        packageId = null;
        return false;
    }

    public static string ResolveOrThrow(Assembly assembly) {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        if (TryGetPackageId(assembly, out string? packageId))
            return packageId;

        throw new InvalidOperationException(
            $"Assembly '{assembly.GetName().Name}' is not registered with RimObs. "
                + "The library auto-registers loaded mods at startup via ModContentPack.PackageId. "
                + "For tests, call OwnerRegistry.RegisterMod(assembly, packageId) explicitly."
        );
    }

    public static int Count {
        get {
            lock (s_Lock) {
                return s_AssemblyToPackageId.Count;
            }
        }
    }

    public static void Clear() {
        lock (s_Lock) {
            s_AssemblyToPackageId.Clear();
            s_LateResolver = null;
        }
    }
}
