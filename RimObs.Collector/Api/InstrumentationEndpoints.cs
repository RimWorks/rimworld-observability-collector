using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class InstrumentationEndpoints {
    public static IEndpointRouteBuilder MapInstrumentationEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/instrumentation/search", async (SessionMetaRegistry registry, string q, int? limit) => {
            if (!registry.IsAvailable)
                return Unavailable();
            ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            ControlSearchResponse res = await client.SearchAsync(new ControlSearchRequest {
                Query = q ?? string.Empty,
                Limit = limit ?? 50,
            });
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                results = res.Results,
            });
        });

        endpoints.MapPost("/api/v1/instrumentation/patch", async (HttpContext ctx, SessionMetaRegistry registry, DynamicPatchStore store) => {
            if (!registry.IsAvailable)
                return Unavailable();
            (ControlPatchRequest? req, IResult? error) = await RequestBody.Read<ControlPatchRequest>(ctx, "patch");
            if (error is not null)
                return error;
            ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            ControlPatchResponse res = await client.PatchAsync(req!);
            if (res.Status == PatchStatus.Active)
                store.Insert(req!.TypeFullName, req.MethodName, string.Join(";", req.ParamTypeFullNames));
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                patch = res,
            });
        });

        endpoints.MapGet("/api/v1/instrumentation/patches", async (SessionMetaRegistry registry, DynamicPatchStore store) => {
            ControlPatchEntry[] live = [];
            if (registry.IsAvailable) {
                ControlClient client = new(registry.ControlPort, registry.ControlSecret);
                ControlPatchListResponse res = await client.ListAsync();
                live = res.Patches;
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                persisted = store.List(),
                live,
            });
        });

        endpoints.MapDelete("/api/v1/instrumentation/patches/{id:long}", async (SessionMetaRegistry registry, DynamicPatchStore store, long id) => {
            if (registry.IsAvailable) {
                ControlClient client = new(registry.ControlPort, registry.ControlSecret);
                await client.UnpatchAsync(id);
            }
            store.Delete(id);
            return Results.NoContent();
        });

        return endpoints;
    }

    private static IResult Unavailable() => Results.Json(new {
        schema_version = SchemaVersion.Current,
        reason = "instrumentation_unavailable",
    }, statusCode: 503);
}
