namespace RimWorks.RimObs.Wire.Control;

/// <summary>How many samples one thread's ring should hold. Rounded up to a power of two.</summary>
public sealed class ControlRingCapacityRequest {
    public int Capacity { get; set; }
}
