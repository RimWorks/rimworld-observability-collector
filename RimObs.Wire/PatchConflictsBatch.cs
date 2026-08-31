namespace RimWorks.RimObs.Wire;

public sealed class PatchConflictsBatch {
    public string[] SectionNames { get; set; } = [];

    public string[] TargetMethods { get; set; } = [];

    public string[] OtherOwners { get; set; } = [];

    public byte[] PatchTypes { get; set; } = [];

    public int[] Priorities { get; set; } = [];

    public string[] PatchMethods { get; set; } = [];
}
