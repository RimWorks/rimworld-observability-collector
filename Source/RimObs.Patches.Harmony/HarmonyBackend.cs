using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorks.RimObs.Patching;

namespace RimWorks.RimObs.Patches.Harmony;

/// <summary>
/// RimObs instrumentation expressed as Harmony patches.
/// </summary>
public sealed class HarmonyBackend : IPatchBackend {
    private const string Id = "RimWorks.RimObs.library";

    private static readonly HarmonyLib.Harmony s_Harmony = new(Id);

    public string Name => "Harmony";

    public void Patch(MethodBase target) {
        HarmonyMethod transpiler = new(MethodTransplanter.TranspilerMethod) { priority = Priority.Low };
        s_Harmony.Patch(target, transpiler: transpiler);
    }

    public void PatchPrefix(MethodBase target, MethodInfo prefix) =>
        s_Harmony.Patch(target, prefix: new HarmonyMethod(prefix));

    public void PatchPostfix(MethodBase target, MethodInfo postfix) =>
        s_Harmony.Patch(target, postfix: new HarmonyMethod(postfix));

    public void Unpatch(MethodBase target) =>
        s_Harmony.Unpatch(target, HarmonyPatchType.Transpiler, Id);

    public IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target) {
        List<ForeignPatch> found = new();
        HarmonyLib.Patches? patches = HarmonyLib.Harmony.GetPatchInfo(target);
        if (patches == null)
            return found;

        Collect(found, patches.Prefixes, PatchKind.Prefix);
        Collect(found, patches.Postfixes, PatchKind.Postfix);
        Collect(found, patches.Transpilers, PatchKind.Transpiler);
        Collect(found, patches.Finalizers, PatchKind.Finalizer);
        return found;
    }

    public void UnpatchAllForTests() {
        try {
            s_Harmony.UnpatchAll(Id);
        }
        catch {
            // swallow: a teardown failure must not mask the real test failure.
        }
    }

    private static void Collect(
        List<ForeignPatch> into, IReadOnlyCollection<Patch> patches, PatchKind kind) {
        foreach (Patch patch in patches) {
            if (patch.owner == Id)
                continue;
            string name = patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name;
            into.Add(new ForeignPatch(patch.owner, kind, patch.priority, name));
        }
    }
}
