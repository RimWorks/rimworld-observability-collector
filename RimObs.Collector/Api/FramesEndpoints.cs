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
                stats = FramePayload.MapStats(stats, usPerTick),
                dropped = new {
                    pre_frame_samples = aggregator.Frames.PreFrameSamples,
                    late_samples = aggregator.Frames.LateSamples,
                },
            });
        });

        return endpoints;
    }
}
