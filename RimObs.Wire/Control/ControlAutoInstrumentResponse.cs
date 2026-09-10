namespace RimWorks.RimObs.Wire.Control;

public sealed class ControlAutoInstrumentResponse {
    public int Matched { get; set; }
    public int Instrumented { get; set; }
    public int Muted { get; set; }
    public int SkippedTrivial { get; set; }
    public int SkippedOther { get; set; }
    public int Refused { get; set; }
    public int Pending { get; set; }
}
