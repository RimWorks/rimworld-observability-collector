using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Storage;

// Write/persist seam only. This exists so the flush path can be spied on in tests;
// it deliberately exposes no read or filesystem-location members. Read sites that need
// the on-disk layout (sessions directory, db path resolution) depend on the concrete
// SqliteSessionPersister directly rather than widening this contract.
public interface ISessionPersister : IDisposable {
    void WriteSessionMeta(SessionMeta meta);

    void WriteSectionsSnapshot(string sessionId, IReadOnlyCollection<SectionStats> sections);

    void WriteMetricsSnapshot(string sessionId, IReadOnlyCollection<MetricStats> metrics);

    void ReplaceGcEventsSnapshot(string sessionId, GcEventRecord[] events);

    void WriteCallTreeSnapshot(string sessionId, IReadOnlyCollection<CallEdgeStats> edges);
}
