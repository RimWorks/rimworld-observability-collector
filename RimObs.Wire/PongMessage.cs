namespace RimWorks.RimObs.Wire;

public sealed class PongMessage {
    public string OwnerId { get; set; } = string.Empty;

    public long PingSentAtUtcTicks { get; set; }

    public string CollectorVersion { get; set; } = string.Empty;

    public string? SessionId { get; set; }
}
