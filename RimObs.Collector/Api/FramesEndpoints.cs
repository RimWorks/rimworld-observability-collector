using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class FramesEndpoints {
    public static IEndpointRouteBuilder MapFramesEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/frames/latest", (SessionAggregator aggregator) => {
            SessionMeta? meta = aggregator.Meta;
            double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
            long anchor = meta?.AnchorTimestamp ?? 0L;
            FrameSnapshot? frame = aggregator.Frames.Latest();
            FrameRingStats stats = aggregator.Frames.ComputeStats();
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                frame = frame is null ? null : MapFrame(frame, anchor, usPerTick),
                stats = new {
                    frame_count = stats.FrameCount,
                    newest_ordinal = stats.NewestOrdinal,
                    oldest_ordinal = stats.OldestOrdinal,
                    median_us = stats.MedianDurationTicks * usPerTick,
                    p99_us = stats.P99DurationTicks * usPerTick,
                    min_us = stats.MinDurationTicks * usPerTick,
                    max_us = stats.MaxDurationTicks * usPerTick,
                },
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                },
            });
        });

        return endpoints;
    }

    private static object MapFrame(FrameSnapshot frame, long anchor, double usPerTick) {
        int n = frame.NodeCount;
        double[] startUs = new double[n];
        double[] durUs = new double[n];
        for (int i = 0; i < n; i++) {
            startUs[i] = (frame.NodeStartTicks[i] - anchor) * usPerTick;
            durUs[i] = frame.NodeElapsedTicks[i] * usPerTick;
        }
        return new {
            capture_ordinal = frame.CaptureOrdinal,
            start_us = (frame.StartTicks - anchor) * usPerTick,
            end_us = (frame.EndTicks - anchor) * usPerTick,
            duration_us = frame.DurationTicks * usPerTick,
            node_count = n,
            nodes = new {
                section_ids = frame.SectionIds,
                parent_ids = frame.ParentIds,
                node_ids = frame.NodeIds,
                parent_node_ids = frame.ParentNodeIds,
                start_us = startUs,
                dur_us = durUs,
            },
        };
    }
}
