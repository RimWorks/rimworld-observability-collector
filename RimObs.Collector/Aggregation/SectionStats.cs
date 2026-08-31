namespace RimWorks.RimObs.Collector.Aggregation;

public sealed class SectionStats {
    public int SectionId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Subsystem { get; set; }
    public long SampleCount;
    public long TotalElapsedTicks;
    public long MinElapsedTicks = long.MaxValue;
    public long MaxElapsedTicks;
    public long LastStartTimestamp;
    public SectionDistribution Distribution { get; } = new();
}
