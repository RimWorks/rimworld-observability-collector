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

        endpoints.MapPost("/api/v1/config", async (HttpContext context, ConfigStore store) => {
            (RimObsConfig? incoming, IResult? error) = await RequestBody.ReadValidated<RimObsConfig>(
                context, RimObsConfig.Version, c => c.SchemaVersion, "config");
            if (error is not null) {
                return error;
            }

            store.Replace(incoming!);
            return Results.Json(store.Current, ConfigJson.Options);
        });

        return endpoints;
    }
}
