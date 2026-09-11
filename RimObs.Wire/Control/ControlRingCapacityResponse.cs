namespace RimWorks.RimObs.Wire.Control;

/// <summary>The per-lane capacity the library settled on, after rounding and clamping.</summary>
public sealed class ControlRingCapacityResponse {
    public int Capacity { get; set; }
}
