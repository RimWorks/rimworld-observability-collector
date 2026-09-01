namespace RimWorks.RimObs.Patching;

public enum PatchKind {
    Prefix,
    Postfix,
    Transpiler,
    Finalizer,
}

internal sealed class PatchConflict {
    public PatchConflict(
        string sectionName,
        string targetMethod,
        string otherOwner,
        PatchKind patchType,
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
    public PatchKind PatchType { get; }
    public int Priority { get; }
    public string PatchMethod { get; }
}
