namespace RimWorks.RimObs.Wire;

public sealed class PingMessage {
    public string OwnerId { get; set; } = string.Empty;

    public long SentAtUtcTicks { get; set; }
}
