using System;
using RimWorks.RimObs.Collector.Aggregation;

namespace RimWorks.RimObs.Collector.Api;

internal static class ReceiveCounters {
    public static object Project(SessionAggregator aggregator) {
        return new {
            total_batches = aggregator.TotalBatches,
            total_samples = aggregator.TotalSamples,
            total_bytes = aggregator.TotalBytes,
            last_batch_utc = aggregator.LastBatchUtc == default ? (DateTime?)null : aggregator.LastBatchUtc,
            section_count = aggregator.SectionCount,
            total_gc_events = aggregator.TotalGcEvents,
            total_allocations = aggregator.TotalAllocations,
            tps = aggregator.HasTpsFps ? aggregator.LatestTps : (double?)null,
            fps = aggregator.HasTpsFps ? aggregator.LatestFps : (double?)null,
            tps_fps_tick = aggregator.HasTpsFps ? aggregator.LatestTpsFpsTick : (long?)null,
        };
    }
}
