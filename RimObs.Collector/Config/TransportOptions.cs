namespace RimWorks.RimObs.Collector.Config;

public sealed class TransportOptions {
    public string WireFormat { get; set; } = "messagepack";
    public bool BatchFlushTickBoundary { get; set; } = true;
    public int BatchFlushBytes { get; set; } = 1024;
    public int BufferOnCollectorLossBytes { get; set; } = 65536;
}
