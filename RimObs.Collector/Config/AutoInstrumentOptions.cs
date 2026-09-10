namespace RimWorks.RimObs.Collector.Config;

public sealed class AutoInstrumentOptions {
    public bool Enabled { get; set; }

    // one Assembly!Type::Method glob per line. a leading ! on a line excludes.
    public string Filters { get; set; } = string.Empty;

    public string Ignore { get; set; } = string.Empty;

    public bool MuteTrivial { get; set; } = true;
}
