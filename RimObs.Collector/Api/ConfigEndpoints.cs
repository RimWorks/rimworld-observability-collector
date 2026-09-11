using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class ConfigEndpoints {
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder endpoints) {
        // returned raw, not enveloped: RimObsConfig already carries its own schema_version. the
        // envelope exists for list payloads that have nowhere of their own to put one.
        endpoints.MapGet("/api/v1/config", (ConfigStore store) =>
            Results.Json(store.Current, ConfigJson.Options));

        endpoints.MapPost("/api/v1/config", async (
            HttpContext context,
            ConfigStore store,
            SessionAggregator aggregator) => {
                (RimObsConfig? incoming, IResult? error) = await RequestBody.ReadValidated<RimObsConfig>(
                    context, RimObsConfig.Version, c => c.SchemaVersion, "config");
                if (error is not null) {
                    return error;
                }

                incoming!.Sampling.MaxCaptureDepth =
                    SamplingOptions.ClampCaptureDepth(incoming.Sampling.MaxCaptureDepth);
                // both of these only ever reach the library, which polls this config. clamp here
                // so a typed-in value cannot hand the game a ring it refuses to allocate.
                incoming.Sampling.RingCapacity =
                    SamplingOptions.ClampRingCapacity(incoming.Sampling.RingCapacity);
                incoming.AutoInstrument.MaxTargets =
                    AutoInstrumentOptions.ClampMaxTargets(incoming.AutoInstrument.MaxTargets);
                store.Replace(incoming);
                // the ring resizes in place so the strip keeps the history that still fits.
                aggregator.Frames.Resize(store.Current.Sampling.FrameRingCapacity);
                aggregator.Frames.OpenFrameWindow = store.Current.Sampling.OpenFrameWindow;
                return Results.Json(store.Current, ConfigJson.Options);
            });

        return endpoints;
    }
}
