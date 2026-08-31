namespace RimWorks.RimObs.Wire;

public sealed class SectionBatch {
    public int[] SectionIds { get; set; } = [];

    public long[] ElapsedTicks { get; set; } = [];

    public long[] StartTimestamps { get; set; } = [];

    public int[] ParentIds { get; set; } = [];
}
