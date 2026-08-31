namespace RimWorks.RimObs.Collector.Config;

public sealed class CaptureOptions {
    public int NestedSectionDepthCap { get; set; } = 10;
    public int NestedSectionTopN { get; set; } = 16;
    public int MaxDurationMinutes { get; set; } = 5;
}
