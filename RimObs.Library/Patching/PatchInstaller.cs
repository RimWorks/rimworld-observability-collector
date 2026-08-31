using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RimWorks.RimObs.Patching;

internal static class PatchInstaller {
    public const string HarmonyId = "RimWorks.RimObs.library";

    private static Harmony? s_Harmony;

    public static int InstalledCount { get; private set; }
    public static int FailedCount { get; private set; }
    public static int UnresolvedCount { get; private set; }

    public static void InstallAll() {
        SectionCatalog.RegisterCorePack();
        SectionCatalog.ResolveAll();

        s_Harmony ??= new Harmony(HarmonyId);

        HarmonyMethod transpiler = new(MethodTransplanter.TranspilerMethod) {
            priority = Priority.Low,
        };

        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (entry.Installed)
                continue;

            if (entry.Resolved == null) {
                UnresolvedCount++;
                continue;
            }

            PatchOne(entry, entry.Resolved, transpiler);
        }

        HarmonyConflictRecorder.RecordConflicts(s_Harmony);
    }

    internal static void PatchAttributeMethod(MethodBase method) {
        if (method == null)
            throw new ArgumentNullException(nameof(method));

        s_Harmony ??= new Harmony(HarmonyId);

        HarmonyMethod transpiler = new(MethodTransplanter.TranspilerMethod) {
            priority = Priority.Low,
        };

        CatalogEntry? entry = null;
        foreach (CatalogEntry e in SectionCatalog.Entries) {
            if (ReferenceEquals(e.Resolved, method)) {
                entry = e;
                break;
            }
        }

        PatchOne(entry, method, transpiler);
    }

    private static void PatchOne(CatalogEntry? entry, MethodBase method, HarmonyMethod transpiler) {
        if (IsUnpatchable(method, out string reason)) {
            if (entry != null)
                entry.InstallError = new NotSupportedException(reason);
            FailedCount++;
            return;
        }

        try {
            s_Harmony!.Patch(method, transpiler: transpiler);
            if (entry != null) {
                entry.Installed = true;
                InstalledCount++;
            }
        }
        catch (Exception ex) {
            if (entry != null)
                entry.InstallError = ex;
            FailedCount++;
        }
    }

    private static bool IsUnpatchable(MethodBase method, out string reason) {
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0) {
            reason = "method is a P/Invoke (extern) and has no patchable IL body";
            return true;
        }
        if ((method.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0) {
            reason = "method is an internal call (intrinsic) and has no patchable IL body";
            return true;
        }
        reason = string.Empty;
        return false;
    }

    public static IReadOnlyList<CatalogEntry> InstalledEntries {
        get {
            List<CatalogEntry> list = new();
            foreach (CatalogEntry entry in SectionCatalog.Entries) {
                if (entry.Installed)
                    list.Add(entry);
            }
            return list;
        }
    }

    public static Harmony? Instance => s_Harmony;

    internal static Harmony EnsureHarmony(string id) {
        s_Harmony ??= new Harmony(id);
        return s_Harmony;
    }

    internal static void PatchSingleForTests(MethodBase target) {
        Harmony harmony = EnsureHarmony("RimWorks.RimObs.tests");
        HarmonyMethod transpiler = new(MethodTransplanter.TranspilerMethod);
        harmony.Patch(target, transpiler: transpiler);
    }

    internal static void ResetForTests() {
        if (s_Harmony != null) {
            try {
                s_Harmony.UnpatchAll(s_Harmony.Id);
            }
            catch {
                // Best-effort cleanup; ignore failures during test teardown.
            }
        }
        s_Harmony = null;
        InstalledCount = 0;
        FailedCount = 0;
        UnresolvedCount = 0;
    }
}
