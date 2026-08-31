namespace RimWorks.RimObs.Wire.Control;

public sealed class ControlSearchRequest {
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; }
}
