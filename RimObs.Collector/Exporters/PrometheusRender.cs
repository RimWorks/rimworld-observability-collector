namespace RimWorks.RimObs.Collector.Exporters;

public readonly struct PrometheusRender {
    public PrometheusRender(string body, int sampleCount) {
        Body = body;
        SampleCount = sampleCount;
    }

    public string Body { get; }
    public int SampleCount { get; }
}
