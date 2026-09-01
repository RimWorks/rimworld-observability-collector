namespace RimWorks.RimObs.Patching;

/// <summary>
/// Another mod's patch on a method RimObs instruments, as the patching library reports it.
/// </summary>
public sealed class ForeignPatch {
    public ForeignPatch(string owner, PatchKind kind, int priority, string patchMethod) {
        Owner = owner;
        Kind = kind;
        Priority = priority;
        PatchMethod = patchMethod;
    }

    public string Owner { get; }
    public PatchKind Kind { get; }
    public int Priority { get; }
    public string PatchMethod { get; }
}
