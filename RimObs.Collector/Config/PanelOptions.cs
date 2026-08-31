namespace RimWorks.RimObs.Collector.Config;

public sealed class PanelOptions {
    public int RefreshFlagPollSeconds { get; set; } = 10;
    public int RefreshFlagTtlSeconds { get; set; } = 30;
}
