namespace RimWorks.RimObs.Patching;

// values are the wire contract the dashboard decodes by index. 0 is All and 5 is Reverse,
// neither of which RimObs emits, so neither gets a member here.
public enum PatchKind {
    Prefix = 1,
    Postfix = 2,
    Transpiler = 3,
    Finalizer = 4,
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
