namespace RimWorks.RimObs.Collector.Config;

public sealed class SamplingOptions {
    public const int DefaultMaxCaptureDepth = 8;
    public const int MinMaxCaptureDepth = 1;
    public const int MaxMaxCaptureDepth = 64;

    public string DefaultMode { get; set; } = "summary";
    public bool DropUnderPressure { get; set; } = true;
    public bool AllocationSamplingEnabled { get; set; } = false;
    public string QuantileSketch { get; set; } = "hdr_histogram";

    // how many captured frames the live strip keeps. the whole ring ships on every frame poll.
    public int FrameRingCapacity { get; set; } = Aggregation.FrameRing.DefaultCapacity;

    // how deep nested profiler sections may nest before the library stops recording them.
    public int MaxCaptureDepth { get; set; } = DefaultMaxCaptureDepth;

    public static int ClampCaptureDepth(int value) {
        int floored = value < MinMaxCaptureDepth ? MinMaxCaptureDepth : value;
        return floored > MaxMaxCaptureDepth ? MaxMaxCaptureDepth : floored;
    }
}
