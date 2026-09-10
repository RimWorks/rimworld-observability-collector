namespace RimWorks.RimObs.Collector.Config;

public sealed class SamplingOptions {
    public string DefaultMode { get; set; } = "summary";
    public bool DropUnderPressure { get; set; } = true;
    public bool AllocationSamplingEnabled { get; set; } = false;
    public string QuantileSketch { get; set; } = "hdr_histogram";

    // how many captured frames the live strip keeps. the whole ring ships on every frame poll.
    public int FrameRingCapacity { get; set; } = Aggregation.FrameRing.DefaultCapacity;
}
