using System.Collections.Generic;
using System.Reflection;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Wire.Control;
using HarmonyLib;

namespace RimWorks.RimObs.Library.Control;

internal sealed class ApplyResult {
    internal ApplyResult(int patchId, int sectionId, string sectionName, PatchStatus status, string? errorReason = null) {
        PatchId = patchId;
        SectionId = sectionId;
        SectionName = sectionName;
        Status = status;
        ErrorReason = errorReason;
    }

    public int PatchId { get; }
    public int SectionId { get; }
    public string SectionName { get; }
    public PatchStatus Status { get; }
    public string? ErrorReason { get; }

    public static ApplyResult Refused(string reason) =>
        new(0, -1, string.Empty, PatchStatus.Refused, reason);
}

internal static class PatchRegistry {
    private const string DynamicSegment = ".dynamic.";

    private static readonly object s_Gate = new object();
    private static readonly Dictionary<string, int> s_BySignature = new();
    private static readonly Dictionary<int, Entry> s_ById = new();
    private static int s_NextId = 1;

    public static ApplyResult Apply(string frameworkPackageId, MethodInfo target, string signature) {
        lock (s_Gate) {
            if (s_BySignature.TryGetValue(signature, out int existingId)) {
                Entry existing = s_ById[existingId];
                return new ApplyResult(existingId, existing.SectionId, existing.SectionName, PatchStatus.Active);
            }

            if (SectionRegistry.Count >= SectionRegistry.MaxSections)
                return ApplyResult.Refused("section cap reached");

            string sectionName = frameworkPackageId + DynamicSegment + signature;
            CatalogEntry catalogEntry = SectionCatalog.RegisterDirect(sectionName, target);
            int sectionId = catalogEntry.SectionId;

            Harmony harmony = PatchInstaller.EnsureHarmony(PatchInstaller.HarmonyId);
            harmony.Patch(target, transpiler: new HarmonyMethod(MethodTransplanter.TranspilerMethod));

            int id = s_NextId++;
            s_BySignature[signature] = id;
            s_ById[id] = new Entry(signature, target, sectionId, sectionName);

            return new ApplyResult(id, sectionId, sectionName, PatchStatus.Active);
        }
    }

    public static bool Remove(int patchId) {
        lock (s_Gate) {
            if (!s_ById.TryGetValue(patchId, out Entry? entry))
                return false;

            Harmony harmony = PatchInstaller.EnsureHarmony(PatchInstaller.HarmonyId);
            harmony.Unpatch(entry.Target, HarmonyPatchType.Transpiler, PatchInstaller.HarmonyId);

            SectionRegistry.SetActive(entry.SectionId, false);
            s_ById.Remove(patchId);
            s_BySignature.Remove(entry.Signature);
            return true;
        }
    }

    public static IEnumerable<(int Id, string Signature, int SectionId, PatchStatus Status)> Snapshot() {
        lock (s_Gate) {
            List<(int, string, int, PatchStatus)> rows = new List<(int, string, int, PatchStatus)>(s_ById.Count);
            foreach (KeyValuePair<int, Entry> kv in s_ById) {
                rows.Add((kv.Key, kv.Value.Signature, kv.Value.SectionId, PatchStatus.Active));
            }
            return rows;
        }
    }

    internal static void ResetForTests() {
        lock (s_Gate) {
            s_BySignature.Clear();
            s_ById.Clear();
            s_NextId = 1;
        }
    }

    private sealed class Entry {
        public Entry(string signature, MethodInfo target, int sectionId, string sectionName) {
            Signature = signature;
            Target = target;
            SectionId = sectionId;
            SectionName = sectionName;
        }

        public string Signature { get; }
        public MethodInfo Target { get; }
        public int SectionId { get; }
        public string SectionName { get; }
    }
}
