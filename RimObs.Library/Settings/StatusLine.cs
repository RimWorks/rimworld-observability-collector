namespace RimWorks.RimObs.Settings;

public sealed class StatusLine {
    public StatusLine(string label, string value, bool healthy) {
        Label = label;
        Value = value;
        Healthy = healthy;
    }

    public string Label { get; }
    public string Value { get; }
    public bool Healthy { get; }
}
