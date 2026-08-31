namespace RimWorks.RimObs.Wire.Control;

public enum PatchStatus : byte {
    Pending = 0,
    Active = 1,
    Refused = 2,
    Stale = 3,
}
