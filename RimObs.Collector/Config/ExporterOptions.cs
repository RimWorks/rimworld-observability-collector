namespace RimWorks.RimObs.Collector.Config;

public sealed class ExporterOptions {
    public bool PrometheusEnabled { get; set; } = false;
    public int PrometheusPort { get; set; } = 7879;
    public bool OtlpEnabled { get; set; } = false;
}
