using System.Collections.Generic;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Patching;

internal static class PatchConflictRecorder {
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
                ConflictsKnown = PatchBackends.Active?.SupportsConflictReporting ?? false,
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

    public static void RecordConflicts() {
        IPatchBackend? backend = PatchBackends.Active;
        if (backend == null)
            return;

        lock (s_Lock) {
            foreach (CatalogEntry entry in SectionCatalog.Entries) {
                if (entry.Resolved == null)
                    continue;

                string target = entry.Resolved.DeclaringType?.FullName + "." + entry.Resolved.Name;
                IReadOnlyList<ForeignPatch> found = backend.ConflictsFor(entry.Resolved);
                for (int i = 0; i < found.Count; i++) {
                    ForeignPatch patch = found[i];
                    s_Conflicts.Add(
                        new PatchConflict(
                            sectionName: entry.Name,
                            targetMethod: target,
                            otherOwner: patch.Owner,
                            patchType: patch.Kind,
                            priority: patch.Priority,
                            patchMethod: patch.PatchMethod
                        )
                    );
                }
            }
        }
    }
}
