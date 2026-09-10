namespace RimWorks.RimObs.Transport;

/// <summary>
/// The nine parallel arrays a drain fills. Allocated once by the sink and reused, so the
/// steady-state drain still allocates nothing.
/// </summary>
public sealed class SampleBatch {
    public SampleBatch(int capacity) {
        Capacity = capacity;
        SectionIds = new int[capacity];
        ParentIds = new int[capacity];
        StartTimestamps = new long[capacity];
        ElapsedTicks = new long[capacity];
        FrameOrdinals = new int[capacity];
        NodeIds = new int[capacity];
        ParentNodeIds = new int[capacity];
        AllocBytes = new long[capacity];
        ThreadIds = new int[capacity];
    }

    public int Capacity { get; }
    public int[] SectionIds { get; }
    public int[] ParentIds { get; }
    public long[] StartTimestamps { get; }
    public long[] ElapsedTicks { get; }
    public int[] FrameOrdinals { get; }
    public int[] NodeIds { get; }
    public int[] ParentNodeIds { get; }
    public long[] AllocBytes { get; }
    public int[] ThreadIds { get; }
}
