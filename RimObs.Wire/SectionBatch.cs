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

    /// <summary>
    /// Lane id per sample, parallel to <see cref="SectionIds"/>. Empty when decoded from a v8
    /// payload, so guard with <c>i &lt; ThreadIds.Length</c>.
    /// </summary>
    public int[] ThreadIds { get; set; } = [];
}
