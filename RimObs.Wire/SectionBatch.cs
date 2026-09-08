namespace RimWorks.RimObs.Wire;

public sealed class SectionBatch {
    public int[] SectionIds { get; set; } = [];

    public long[] ElapsedTicks { get; set; } = [];

    public long[] StartTimestamps { get; set; } = [];

    public int[] ParentIds { get; set; } = [];

    public int[] FrameOrdinals { get; set; } = [];

    public int[] NodeIds { get; set; } = [];

    public int[] ParentNodeIds { get; set; } = [];

    public long[] AllocBytes { get; set; } = [];
}
