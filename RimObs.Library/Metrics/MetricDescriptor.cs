using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Metrics;

internal sealed class MetricDescriptor {
    public const int DefaultCardinalityLimit = 64;
    public const string OverflowLabel = "__overflow";

    public MetricDescriptor(int id, string fullName, string ownerPackageId, MetricKind kind, string? subsystem, string? unit, int cardinalityLimit = DefaultCardinalityLimit) {
        Id = id;
        FullName = fullName;
        OwnerPackageId = ownerPackageId;
        Kind = kind;
        Subsystem = subsystem;
        Unit = unit;
        CardinalityLimit = cardinalityLimit;
    }

    public int Id { get; }
    public string FullName { get; }
    public string OwnerPackageId { get; }
    public MetricKind Kind { get; }
    public string? Subsystem { get; }
    public string? Unit { get; }
    public int CardinalityLimit { get; }

    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, MetricLabelEntry> LabeledEntries = new(System.StringComparer.Ordinal);

    internal long CounterTotal;
    internal long GaugeValue;
    internal long HistogramObservationCount;
    internal long HistogramSum;
    internal long CardinalityIncidentCount;
}
