namespace RimWorks.RimObs.Metrics;

public readonly struct MetricCardinalityIncident {
    public MetricCardinalityIncident(string metricName, string ownerPackageId, int cardinalityLimit, long incidentCount) {
        MetricName = metricName;
        OwnerPackageId = ownerPackageId;
        CardinalityLimit = cardinalityLimit;
        IncidentCount = incidentCount;
    }

    public string MetricName { get; }
    public string OwnerPackageId { get; }
    public int CardinalityLimit { get; }
    public long IncidentCount { get; }
}
