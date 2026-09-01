namespace RimWorks.RimObs.Logging;

/// <summary>
/// RimLogging channel names. Consts because a typo in a channel string silently creates a new
/// channel instead of failing.
/// </summary>
public static class LogChannels {
    public const string Root = "RimObs";
    public const string Bootstrap = "RimObs.Bootstrap";
    public const string Collector = "RimObs.Collector";
    public const string Patching = "RimObs.Patching";
    public const string Sections = "RimObs.Sections";
}
