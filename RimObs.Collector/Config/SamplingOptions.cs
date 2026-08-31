namespace RimWorks.RimObs.Collector.Config;

public sealed class SamplingOptions {
    public string DefaultMode { get; set; } = "summary";
    public bool FocusedCaptureEnabled { get; set; } = true;
    public bool DropUnderPressure { get; set; } = true;
    public bool AllocationSamplingEnabled { get; set; } = false;
    public string QuantileSketch { get; set; } = "hdr_histogram";
}
