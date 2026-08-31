namespace RimWorks.RimObs.Collector.Config;

public sealed class SessionOptions {
    public bool SplitSessionOnSaveLoad { get; set; } = false;
    public int SlowTickThresholdUs { get; set; } = 16667;
}
