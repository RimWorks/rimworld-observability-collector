using RimWorks.RimObs.Collector.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class ConfigEndpoints {
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder endpoints) {
        // The config resource is returned raw rather than under a { schema_version, ... } envelope:
        // RimObsConfig already carries its own schema_version field (the same field the POST handler
        // validates against RimObsConfig.Version). It is a single self-versioned object, so an outer
        // envelope would just duplicate that field. The envelope pattern exists for list/aggregate
        // payloads (arrays) that have no natural place to carry their own version.
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
