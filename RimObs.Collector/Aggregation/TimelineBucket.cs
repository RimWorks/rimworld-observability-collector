namespace RimWorks.RimObs.Collector.Aggregation;

public readonly record struct TimelineBucket(long EpochSeconds, long Count, long TotalTicks);
