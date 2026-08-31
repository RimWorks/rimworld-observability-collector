namespace RimWorks.RimObs.Wire;

public sealed class MetricsBatch {
    public int[] MetricIds { get; set; } = [];

    public string[] LabelCanonicals { get; set; } = [];

    public byte[] Kinds { get; set; } = [];

    public long[] Values { get; set; } = [];

    public long[] SampleCounts { get; set; } = [];
}
