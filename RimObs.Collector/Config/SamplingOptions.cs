namespace RimWorks.RimObs.Collector.Config;

public sealed class SamplingOptions {
    public const int DefaultMaxCaptureDepth = 8;
    public const int MinMaxCaptureDepth = 1;
    public const int MaxMaxCaptureDepth = 64;

    public const int DefaultRingCapacity = 16384;
    public const int MinRingCapacity = 1024;
    public const int MaxRingCapacity = 1048576;

    public string DefaultMode { get; set; } = "summary";
    public bool DropUnderPressure { get; set; } = true;
    public bool AllocationSamplingEnabled { get; set; } = false;
    public string QuantileSketch { get; set; } = "hdr_histogram";

    // how many captured frames the live strip keeps. the whole ring ships on every frame poll.
    public int FrameRingCapacity { get; set; } = Aggregation.FrameRing.DefaultCapacity;

    // how many newer frames land before one seals, which is the room a late thread lane gets.
    public int OpenFrameWindow { get; set; } = Aggregation.FrameRing.DefaultOpenFrameWindow;

    // how deep nested profiler sections may nest before the library stops recording them.
    public int MaxCaptureDepth { get; set; } = DefaultMaxCaptureDepth;

    // slots in each of the library's per-thread sample rings. a wide-open auto-instrument filter
    // can put 70k samples in one frame, and anything past this is dropped mid-frame.
    public int RingCapacity { get; set; } = DefaultRingCapacity;

    public static int ClampCaptureDepth(int value) {
        int floored = value < MinMaxCaptureDepth ? MinMaxCaptureDepth : value;
        return floored > MaxMaxCaptureDepth ? MaxMaxCaptureDepth : floored;
    }

    /// <summary>Clamps to the supported range, then rounds up to a power of two.</summary>
    public static int ClampRingCapacity(int value) {
        int floored = value < MinRingCapacity ? MinRingCapacity : value;
        int capped = floored > MaxRingCapacity ? MaxRingCapacity : floored;
        int rounded = MinRingCapacity;
        while (rounded < capped) {
            rounded <<= 1;
        }
        return rounded;
    }
}
