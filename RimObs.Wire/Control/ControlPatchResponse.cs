namespace RimWorks.RimObs.Wire.Control;

public sealed class ControlPatchResponse {
    public int PatchId { get; set; }
    public int SectionId { get; set; }
    public string SectionName { get; set; } = string.Empty;
    public PatchStatus Status { get; set; }
    public string? ErrorReason { get; set; }
}
