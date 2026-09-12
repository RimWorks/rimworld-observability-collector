namespace RimWorks.RimObs.Collector.Aggregation;

/// <summary>
/// The frame json shape. Both the live endpoint and the bundle entry go through here so the
/// stored shape and the served shape cannot drift.
/// </summary>
public static class FramePayload {
    public static object Map(FrameSnapshot frame, long anchor, double usPerTick) {
        int n = frame.NodeCount;
        double[] startUs = new double[n];
        double[] durUs = new double[n];
        // one decimal: timer resolution is 100ns anyway, and shortest-roundtrip doubles like
        // 194435307.20000002 tripled the json size of every hot array.
        for (int i = 0; i < n; i++) {
            startUs[i] = System.Math.Round((frame.NodeStartTicks[i] - anchor) * usPerTick, 1);
            durUs[i] = System.Math.Round(frame.NodeElapsedTicks[i] * usPerTick, 1);
        }
        return new {
            capture_ordinal = frame.CaptureOrdinal,
            start_us = System.Math.Round((frame.StartTicks - anchor) * usPerTick, 1),
            end_us = System.Math.Round((frame.EndTicks - anchor) * usPerTick, 1),
            duration_us = System.Math.Round(frame.DurationTicks * usPerTick, 1),
            node_count = n,
            nodes = new {
                section_ids = frame.SectionIds,
                parent_ids = frame.ParentIds,
                node_ids = frame.NodeIds,
                parent_node_ids = frame.ParentNodeIds,
                start_us = startUs,
                dur_us = durUs,
                alloc_bytes = frame.NodeAllocBytes,
                thread_ids = frame.ThreadIds,
            },
        };
    }

    public static object MapStats(FrameRingStats stats, double usPerTick) {
        return new {
            frame_count = stats.FrameCount,
            newest_ordinal = stats.NewestOrdinal,
            oldest_ordinal = stats.OldestOrdinal,
            median_us = stats.MedianDurationTicks * usPerTick,
            p75_us = stats.P75DurationTicks * usPerTick,
            p90_us = stats.P90DurationTicks * usPerTick,
            p99_us = stats.P99DurationTicks * usPerTick,
            min_us = stats.MinDurationTicks * usPerTick,
            max_us = stats.MaxDurationTicks * usPerTick,
        };
    }
}
