using System.Collections.Generic;
using System.Threading;
using RimWorks.RimObs.Metrics;

namespace RimWorks.RimObs.Api;

public static class Diagnostics {
    private static long s_SamplesDroppedExternal;

    public static long SamplesDroppedExternal => Interlocked.Read(ref s_SamplesDroppedExternal);

    public static void IncrementSamplesDropped(long count = 1) {
        Interlocked.Add(ref s_SamplesDroppedExternal, count);
    }

    public static long CardinalityIncidentsTotal {
        get {
            long total = 0;
            int count = MetricRegistry.Count;
            for (int i = 0; i < count; i++) {
                MetricDescriptor? descriptor = MetricRegistry.Get(i);
                if (descriptor == null)
                    continue;
                total += Interlocked.Read(ref descriptor.CardinalityIncidentCount);
            }
            return total;
        }
    }

    public static IEnumerable<MetricCardinalityIncident> GetMetricsWithIncidents() {
        int count = MetricRegistry.Count;
        for (int i = 0; i < count; i++) {
            MetricDescriptor? descriptor = MetricRegistry.Get(i);
            if (descriptor == null)
                continue;
            long incidents = Interlocked.Read(ref descriptor.CardinalityIncidentCount);
            if (incidents > 0)
                yield return new MetricCardinalityIncident(
                    descriptor.FullName,
                    descriptor.OwnerPackageId,
                    descriptor.CardinalityLimit,
                    incidents
                );
        }
    }

    /// <summary>
    /// Resets incident counters only: externally-reported samples-dropped and each descriptor's
    /// cardinality count. Metric values are untouched. Test isolation, not production.
    /// </summary>
    public static void Reset() {
        Interlocked.Exchange(ref s_SamplesDroppedExternal, 0);
        int count = MetricRegistry.Count;
        for (int i = 0; i < count; i++) {
            MetricDescriptor? descriptor = MetricRegistry.Get(i);
            if (descriptor == null)
                continue;
            Interlocked.Exchange(ref descriptor.CardinalityIncidentCount, 0);
        }
    }
}


