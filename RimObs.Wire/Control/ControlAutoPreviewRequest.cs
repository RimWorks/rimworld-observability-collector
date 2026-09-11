namespace RimWorks.RimObs.Wire.Control;

/// <summary>A filter list to count against the loaded assemblies, patching nothing.</summary>
public sealed class ControlAutoPreviewRequest {
    public string Filters { get; set; } = string.Empty;

    public string Ignore { get; set; } = string.Empty;

    /// <summary>Cap on queued targets. Zero means the cap the library is configured with.</summary>
    public int MaxTargets { get; set; }
}
