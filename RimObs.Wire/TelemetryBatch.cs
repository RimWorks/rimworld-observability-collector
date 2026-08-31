namespace RimWorks.RimObs.Wire;

public sealed class TelemetryBatch {
    public int SchemaVersion { get; set; }

    public ulong Sequence { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public BatchType BatchType { get; set; }

    public byte[] Payload { get; set; } = [];
}
