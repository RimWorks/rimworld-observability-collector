using System.Collections.Concurrent;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Aggregation;

public sealed class MetricStats {
    public MetricStats(int metricId) {
        MetricId = metricId;
        Labels = new ConcurrentDictionary<string, MetricLabelStats>();
    }

    public int MetricId { get; }
    public string Name { get; set; } = string.Empty;
    public MetricKind Kind { get; set; }
    public string Unit { get; set; } = string.Empty;
    public ConcurrentDictionary<string, MetricLabelStats> Labels { get; }
}
