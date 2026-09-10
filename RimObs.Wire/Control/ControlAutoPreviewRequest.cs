namespace RimWorks.RimObs.Wire.Control;

/// <summary>A filter list to count against the loaded assemblies, patching nothing.</summary>
public sealed class ControlAutoPreviewRequest {
    public string Filters { get; set; } = string.Empty;

    public string Ignore { get; set; } = string.Empty;
}
