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
                stopwatch_frequency = meta?.StopwatchFrequency ?? 0L,
                frame = frame is null ? null : FramePayload.Map(frame, anchor, usPerTick),
                strip = MapStrip(aggregator, usPerTick, 240),
                stats = FramePayload.MapStats(stats, usPerTick),
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                },
            });
        });

        endpoints.MapGet("/api/v1/frames/{ordinal:int}", (SessionAggregator aggregator, int ordinal) => {
            SessionMeta? meta = aggregator.Meta;
            double usPerTick = TickConverter.NsPerTick(meta) / 1000.0;
            long anchor = meta?.AnchorTimestamp ?? 0L;
            FrameSnapshot? frame = aggregator.Frames.FindByOrdinal(ordinal);
            if (frame is null)
                return Results.NotFound(new { error = "no frame with that ordinal is in the ring" });
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                stopwatch_frequency = meta?.StopwatchFrequency ?? 0L,
                frame = FramePayload.Map(frame, anchor, usPerTick),
                strip = MapStrip(aggregator, usPerTick, 240),
                stats = FramePayload.MapStats(aggregator.Frames.ComputeStats(), usPerTick),
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                },
            });
        });

        return endpoints;
    }

    private static object MapStrip(SessionAggregator aggregator, double usPerTick, int count) {
        (int Ordinal, long DurationTicks)[] strip = aggregator.Frames.SnapshotStrip(count);
        int[] ordinals = new int[strip.Length];
        double[] durations = new double[strip.Length];
        for (int i = 0; i < strip.Length; i++) {
            ordinals[i] = strip[i].Ordinal;
            durations[i] = strip[i].DurationTicks * usPerTick;
        }
        return new { ordinals, durations_us = durations };
    }
}
