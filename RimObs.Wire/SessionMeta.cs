namespace RimWorks.RimObs.Wire;

public sealed class SessionMeta {
    public string SessionId { get; set; } = string.Empty;
    public long StartedUtcTicks { get; set; }
    public long StopwatchFrequency { get; set; }
    public long AnchorTimestamp { get; set; }
    public string LibraryVersion { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public int ControlPort { get; set; }
    public string ControlSecret { get; set; } = string.Empty;
}
