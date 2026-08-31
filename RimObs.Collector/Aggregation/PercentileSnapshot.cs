namespace RimWorks.RimObs.Collector.Aggregation;

public readonly record struct PercentileSnapshot(long P50Ticks, long P95Ticks, long P99Ticks) {
    public static readonly PercentileSnapshot Empty = new(0, 0, 0);
}
