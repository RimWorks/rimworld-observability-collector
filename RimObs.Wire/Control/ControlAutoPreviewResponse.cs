namespace RimWorks.RimObs.Wire.Control;

/// <summary>
/// What a filter list would do if applied. <see cref="Eligible"/> is the number that would
/// actually take a timing scope; everything else matched and was dropped for the named reason.
/// </summary>
public sealed class ControlAutoPreviewResponse {
    public int Matched { get; set; }

    public int Eligible { get; set; }

    public int SkippedTrivial { get; set; }

    public int SkippedIgnored { get; set; }

    public int SkippedBlocklisted { get; set; }

    public int SkippedAlreadyInstrumented { get; set; }

    public int SkippedOverCap { get; set; }
}
