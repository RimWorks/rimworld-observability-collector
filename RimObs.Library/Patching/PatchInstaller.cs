using System;
using System.Collections.Generic;
using System.Reflection;

namespace RimWorks.RimObs.Patching;

internal static class PatchInstaller {
    public static int InstalledCount { get; private set; }
    public static int FailedCount { get; private set; }
    public static int UnresolvedCount { get; private set; }

    public static void InstallAll() {
        if (PatchBackends.Active == null)
            return;

        SectionCatalog.RegisterCorePack();
        SectionCatalog.ResolveAll();

        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (entry.Installed)
                continue;

            if (entry.Resolved == null) {
                UnresolvedCount++;
                continue;
            }

            PatchOne(entry, entry.Resolved);
        }

        PatchConflictRecorder.RecordConflicts();
    }

    internal static void PatchAttributeMethod(MethodBase method) {
        if (method == null)
            throw new ArgumentNullException(nameof(method));

        CatalogEntry? entry = null;
        foreach (CatalogEntry e in SectionCatalog.Entries) {
            if (ReferenceEquals(e.Resolved, method)) {
                entry = e;
                break;
            }
        }

        PatchOne(entry, method);
    }

    private static void PatchOne(CatalogEntry? entry, MethodBase method) {
        IPatchBackend? backend = PatchBackends.Active;
        if (backend == null) {
            FailedCount++;
            return;
        }

        if (IsUnpatchable(method, out string reason)) {
            if (entry != null)
                entry.InstallError = new NotSupportedException(reason);
            FailedCount++;
            return;
        }

        try {
            backend.Patch(method);
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

    internal static void ResetForTests() {
        PatchBackends.Active?.UnpatchAllForTests();
        InstalledCount = 0;
        FailedCount = 0;
        UnresolvedCount = 0;
    }
}
