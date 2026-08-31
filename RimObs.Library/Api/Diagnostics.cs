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
    /// Resets diagnostic incident counters only: the externally-reported samples-dropped counter
    /// and every metric descriptor's cardinality incident counter. Metric values themselves
    /// (counter sums, gauge readings, histogram buckets) are NOT touched.
    /// Intended for test isolation, not for production runtime use.
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


