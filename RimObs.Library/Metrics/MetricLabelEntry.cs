namespace RimWorks.RimObs.Metrics;

internal sealed class MetricLabelEntry {
    public MetricLabelEntry(string canonicalLabel) {
        CanonicalLabel = canonicalLabel;
    }

    public string CanonicalLabel { get; }

    internal long CounterTotal;
    internal long GaugeValue;
    internal long HistogramObservationCount;
    internal long HistogramSum;
}
