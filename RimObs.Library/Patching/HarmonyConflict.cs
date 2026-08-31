using HarmonyLib;

namespace RimWorks.RimObs.Patching;

internal sealed class HarmonyConflict {
    public HarmonyConflict(
        string sectionName,
        string targetMethod,
        string otherOwner,
        HarmonyPatchType patchType,
        int priority,
        string patchMethod
    ) {
        SectionName = sectionName;
        TargetMethod = targetMethod;
        OtherOwner = otherOwner;
        PatchType = patchType;
        Priority = priority;
        PatchMethod = patchMethod;
    }

    public string SectionName { get; }
    public string TargetMethod { get; }
    public string OtherOwner { get; }
    public HarmonyPatchType PatchType { get; }
    public int Priority { get; }
    public string PatchMethod { get; }
}
