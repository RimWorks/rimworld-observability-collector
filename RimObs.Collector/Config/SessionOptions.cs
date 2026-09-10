namespace RimWorks.RimObs.Collector.Config;

public sealed class SessionOptions {
    public bool SplitSessionOnSaveLoad { get; set; } = false;
    public int SlowTickThresholdUs { get; set; } = 16667;

    // A name typed before a restart. The collector dies with the game, so this rides on disk and
    // the next session that appears claims it. Empty means nothing is waiting.
    public string PendingName { get; set; } = string.Empty;

    // Whether the dashboard asks for a name when an unnamed session starts.
    public bool PromptForName { get; set; } = true;
}
