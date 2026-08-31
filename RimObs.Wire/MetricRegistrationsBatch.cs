namespace RimWorks.RimObs.Wire;

public sealed class MetricRegistrationsBatch {
    public int[] MetricIds { get; set; } = [];

    public string[] Names { get; set; } = [];

    public byte[] Kinds { get; set; } = [];

    public string[] Units { get; set; } = [];
}
