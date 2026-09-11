namespace RimWorks.RimObs.Wire.Control;

public sealed class ControlAutoInstrumentResponse {
    public int Matched { get; set; }
    public int Instrumented { get; set; }
    public int Muted { get; set; }
    public int SkippedTrivial { get; set; }
    public int SkippedOther { get; set; }
    public int Refused { get; set; }
    public int Pending { get; set; }
    public int SkippedOverCap { get; set; }
    public int MaxTargets { get; set; }

    /// <summary>The cap bit: eligible methods were dropped from the plan.</summary>
    public bool Truncated => SkippedOverCap > 0;
}
