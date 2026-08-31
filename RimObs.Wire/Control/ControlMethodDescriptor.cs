namespace RimWorks.RimObs.Wire.Control;

public sealed class ControlMethodDescriptor {
    public string TypeFullName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string[] ParamTypeFullNames { get; set; } = [];
    public string AssemblyName { get; set; } = string.Empty;
}
