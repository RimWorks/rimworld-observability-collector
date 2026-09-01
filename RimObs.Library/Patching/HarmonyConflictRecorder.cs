using System.Collections.Generic;
using RimWorks.RimObs.Wire;
using HarmonyLib;

namespace RimWorks.RimObs.Patching;

internal static class HarmonyConflictRecorder {
    private static readonly List<PatchConflict> s_Conflicts = new();
    private static readonly object s_Lock = new();

    public static IReadOnlyList<PatchConflict> Conflicts {
        get {
            lock (s_Lock) {
                return s_Conflicts.ToArray();
            }
        }
    }

    public static int Count {
        get {
            lock (s_Lock) {
                return s_Conflicts.Count;
            }
        }
    }

    public static void Clear() {
        lock (s_Lock) {
            s_Conflicts.Clear();
        }
    }

    public static PatchConflictsBatch BuildBatch() {
        lock (s_Lock) {
            int n = s_Conflicts.Count;
            PatchConflictsBatch batch = new() {
                SectionNames = new string[n],
                TargetMethods = new string[n],
                OtherOwners = new string[n],
                PatchTypes = new byte[n],
                Priorities = new int[n],
                PatchMethods = new string[n],
            };
            for (int i = 0; i < n; i++) {
                PatchConflict conflict = s_Conflicts[i];
                batch.SectionNames[i] = conflict.SectionName;
                batch.TargetMethods[i] = conflict.TargetMethod;
                batch.OtherOwners[i] = conflict.OtherOwner;
                batch.PatchTypes[i] = (byte)conflict.PatchType;
                batch.Priorities[i] = conflict.Priority;
                batch.PatchMethods[i] = conflict.PatchMethod;
            }
            return batch;
        }
    }

    public static void RecordConflicts(Harmony harmony) {
        lock (s_Lock) {
            foreach (CatalogEntry entry in SectionCatalog.Entries) {
                if (entry.Resolved == null)
                    continue;

                Patches? patches = Harmony.GetPatchInfo(entry.Resolved);
                if (patches == null)
                    continue;

                RecordList(entry, patches.Prefixes, PatchKind.Prefix, harmony.Id);
                RecordList(entry, patches.Postfixes, PatchKind.Postfix, harmony.Id);
                RecordList(entry, patches.Transpilers, PatchKind.Transpiler, harmony.Id);
                RecordList(entry, patches.Finalizers, PatchKind.Finalizer, harmony.Id);
            }
        }
    }

    private static void RecordList(CatalogEntry entry, IReadOnlyCollection<Patch> patches, PatchKind kind, string ownId) {
        if (patches == null)
            return;

        foreach (Patch patch in patches) {
            if (patch.owner == ownId)
                continue;

            string target = entry.Resolved!.DeclaringType?.FullName + "." + entry.Resolved.Name;
            string patchMethodName = patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name;
            s_Conflicts.Add(
                new PatchConflict(
                    sectionName: entry.Name,
                    targetMethod: target,
                    otherOwner: patch.owner,
                    patchType: kind,
                    priority: patch.priority,
                    patchMethod: patchMethodName
                )
            );
        }
    }
}
