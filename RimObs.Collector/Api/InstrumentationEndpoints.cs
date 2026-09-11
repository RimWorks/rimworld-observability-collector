using System.Net.Http;
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
            ControlSearchResponse res;
            try {
                res = await client.SearchAsync(new ControlSearchRequest {
                    Query = q ?? string.Empty,
                    Limit = limit ?? 50,
                });
            }
            catch (ControlClientException ex) {
                return ControlFailed(ex);
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                results = res.Results,
            });
        });

        endpoints.MapGet("/api/v1/instrumentation/assemblies", async (SessionMetaRegistry registry) => {
            if (!registry.IsAvailable)
                return Unavailable();
            ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            ControlAssembliesResponse res;
            try {
                res = await client.AssembliesAsync();
            }
            catch (ControlClientException ex) {
                return ControlFailed(ex);
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                assemblies = res.Assemblies,
            });
        });

        endpoints.MapGet("/api/v1/instrumentation/auto", async (SessionMetaRegistry registry) => {
            if (!registry.IsAvailable)
                return Unavailable();
            ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            ControlAutoInstrumentResponse res;
            try {
                res = await client.AutoInstrumentAsync();
            }
            catch (ControlClientException ex) {
                return ControlFailed(ex);
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                auto = res,
            });
        });

        // a dry run, so the dashboard can say how wide a filter is before it reaches the game.
        endpoints.MapPost("/api/v1/instrumentation/auto/preview", async (HttpContext ctx, SessionMetaRegistry registry) => {
            if (!registry.IsAvailable)
                return Unavailable();
            (ControlAutoPreviewRequest? req, IResult? error) = await RequestBody.Read<ControlAutoPreviewRequest>(ctx, "auto preview");
            if (error is not null)
                return error;
            ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            ControlAutoPreviewResponse res;
            try {
                res = await client.AutoPreviewAsync(req!);
            }
            catch (ControlClientException ex) {
                return ControlFailed(ex);
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                preview = res,
            });
        });

        endpoints.MapPost("/api/v1/instrumentation/patch", async (HttpContext ctx, SessionMetaRegistry registry, DynamicPatchStore store) => {
            if (!registry.IsAvailable)
                return Unavailable();
            (ControlPatchRequest? req, IResult? error) = await RequestBody.Read<ControlPatchRequest>(ctx, "patch");
            if (error is not null)
                return error;
            ControlClient client = new(registry.ControlPort, registry.ControlSecret);
            ControlPatchResponse res;
            try {
                res = await client.PatchAsync(req!);
            }
            catch (ControlClientException ex) {
                return ControlFailed(ex);
            }
            if (res.Status == PatchStatus.Active) {
                long rowId = store.Insert(req!.TypeFullName, req.MethodName, string.Join(";", req.ParamTypeFullNames));
                store.UpdateLivePatchId(rowId, res.PatchId);
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                patch = res,
            });
        });

        endpoints.MapGet("/api/v1/instrumentation/patches", async (SessionMetaRegistry registry, DynamicPatchStore store) => {
            ControlPatchEntry[] live = [];
            if (registry.IsAvailable) {
                ControlClient client = new(registry.ControlPort, registry.ControlSecret);
                // persisted rows come from our own sqlite, so a control blip must not drop the
                // whole list. no live entry already reads as stale downstream.
                try {
                    ControlPatchListResponse res = await client.ListAsync();
                    live = res.Patches;
                }
                catch (Exception ex) when (ex is ControlClientException or HttpRequestException or TaskCanceledException) {
                    live = [];
                }
            }
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                persisted = store.List(),
                live,
            });
        });

        endpoints.MapDelete("/api/v1/instrumentation/patches/{id:long}", async (SessionMetaRegistry registry, DynamicPatchStore store, long id) => {
            // the library renumbers its patch ids from 1 every launch, so the row id is not a
            // valid handle for it. only the recorded live id is.
            int? livePatchId = store.Find(id)?.LivePatchId;
            if (registry.IsAvailable && livePatchId is not null) {
                ControlClient client = new(registry.ControlPort, registry.ControlSecret);
                try {
                    await client.UnpatchAsync(livePatchId.Value);
                }
                catch (ControlClientException ex) {
                    // the patch is still live in the game, so keep the row and let the caller retry.
                    return ControlFailed(ex);
                }
            }
            store.Delete(id);
            return Results.NoContent();
        });

        return endpoints;
    }

    // a drain timeout means the game is paused or wedged; anything else is the control endpoint
    // itself failing, and neither should surface as an empty 500.
    private static IResult ControlFailed(ControlClientException ex) => Results.Json(new {
        schema_version = SchemaVersion.Current,
        reason = ex.Status == 504 ? "instrumentation_timeout" : "instrumentation_failed",
        detail = ex.Reason,
    }, statusCode: ex.Status == 504 ? 504 : 502);

    private static IResult Unavailable() => Results.Json(new {
        schema_version = SchemaVersion.Current,
        reason = "instrumentation_unavailable",
    }, statusCode: 503);
}
