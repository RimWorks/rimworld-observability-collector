namespace RimWorks.RimObs.Collector.Aggregation;

public sealed class CallEdgeStats {
    public int ParentId { get; init; }
    public int SectionId { get; init; }
    public long CallCount;
    public long TotalElapsedTicks;
}
