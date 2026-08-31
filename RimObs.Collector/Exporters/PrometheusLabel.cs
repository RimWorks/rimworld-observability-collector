namespace RimWorks.RimObs.Collector.Exporters;

public readonly struct PrometheusLabel {
    public PrometheusLabel(string name, string value) {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string Value { get; }
}
