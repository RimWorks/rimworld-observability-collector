namespace RimWorks.RimObs.Wire.Control;

public sealed class ControlPatchRequest {
    public string TypeFullName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string[] ParamTypeFullNames { get; set; } = [];
}
