using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Bundle;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class BundleEndpoints {
    public static IEndpointRouteBuilder MapBundleEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapPost("/api/v1/export/bundle", ExportBundle);
        endpoints.MapPost("/api/v1/export/bundle/estimate", EstimateBundle);
        endpoints.MapPost("/api/v1/import/bundle", ImportBundle);
        endpoints.MapGet("/api/v1/import/bundle/{token}/file/{name}", GetImportedFile);
        endpoints.MapDelete("/api/v1/import/bundle/{token}", DeleteImported);
        return endpoints;
    }

    private static async Task<IResult> ExportBundle(HttpContext ctx, BundleExportService service) {
        (BundleExportRequestDto? dto, IResult? error) = await RequestBody.Read<BundleExportRequestDto>(ctx, "bundle export");
        if (error is not null)
            return error;
        if (string.IsNullOrEmpty(dto!.SessionId))
            return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "missing session_id" });

        HashSet<BundleContentKey> includes = ParseIncludes(dto.Include);
        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = dto.SessionId,
            Includes = includes,
            Force = dto.Force,
        }, ctx.RequestAborted);

        return result.Status switch {
            BundleExportStatus.Ok => Results.File(result.Bytes!, "application/zip", $"{dto.SessionId}.rimobs.zip"),
            BundleExportStatus.UnknownSession => Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "unknown session" }),
            BundleExportStatus.NoActiveSession => Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no active session" }),
            BundleExportStatus.ExceedsSoftCap => Results.Json(new {
                schema_version = SchemaVersion.Current,
                error = "estimate_exceeds_soft_cap",
                estimated_bytes = result.EstimatedBytes,
                cap_bytes = BundleSizeEstimator.SoftCapBytes,
                hint = "retry with force=true",
            }, statusCode: StatusCodes.Status413PayloadTooLarge),
            _ => Results.StatusCode(500),
        };
    }

    private static async Task<IResult> EstimateBundle(HttpContext ctx, BundleExportService service) {
        (BundleExportRequestDto? dto, IResult? error) = await RequestBody.Read<BundleExportRequestDto>(ctx, "bundle estimate");
        if (error is not null)
            return error;
        if (string.IsNullOrEmpty(dto!.SessionId))
            return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "missing session_id" });

        HashSet<BundleContentKey> includes = ParseIncludes(dto.Include);
        BundleEstimateResult result = service.Estimate(dto.SessionId, includes);

        return result.Status switch {
            BundleExportStatus.Ok => Results.Ok(new {
                schema_version = SchemaVersion.Current,
                estimated_bytes = result.EstimatedBytes,
                cap_bytes = BundleSizeEstimator.SoftCapBytes,
                exceeds_soft_cap = result.ExceedsSoftCap,
            }),
            BundleExportStatus.UnknownSession => Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "unknown session" }),
            BundleExportStatus.NoActiveSession => Results.NotFound(new { schema_version = SchemaVersion.Current, reason = "no active session" }),
            _ => Results.StatusCode(500),
        };
    }

    private static async Task<IResult> ImportBundle(HttpContext ctx, BundleImportService importer) {
        if (!ctx.Request.HasFormContentType)
            return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "expected multipart/form-data" });

        IFormCollection form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        if (form.Files.Count == 0)
            return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "no file uploaded" });

        IFormFile file = form.Files[0];
        using Stream stream = file.OpenReadStream();
        BundleImportResult result = await importer.ImportAsync(stream);
        return result.Status switch {
            BundleImportStatus.Ok => Results.Ok(new {
                schema_version = SchemaVersion.Current,
                token = result.Entry!.Token,
                manifest = result.Manifest,
                contents = result.Entry.Contents,
            }),
            BundleImportStatus.MissingManifest => Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "missing manifest.json" }),
            BundleImportStatus.InvalidArchive => Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = "invalid archive" }),
            _ => Results.StatusCode(500),
        };
    }

    private static IResult GetImportedFile(string token, string name, BundleImportRegistry registry) {
        if (!registry.TryGet(token, out BundleImportEntry? entry) || entry is null)
            return Results.NotFound();
        registry.Touch(token);
        string safePath = Path.Combine(entry.TempDir, Path.GetFileName(name));
        if (!File.Exists(safePath)) return Results.NotFound();

        string contentType = "application/json";
        if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            contentType = "text/html";
        else if (name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
            contentType = "application/octet-stream";
        return Results.File(File.ReadAllBytes(safePath), contentType);
    }

    private static IResult DeleteImported(string token, BundleImportRegistry registry) {
        return registry.Remove(token) ? Results.NoContent() : Results.NotFound();
    }

    private static HashSet<BundleContentKey> ParseIncludes(string[]? values) {
        HashSet<BundleContentKey> set = new HashSet<BundleContentKey>();
        if (values is null) return set;
        foreach (string v in values) {
            BundleContentKey? key = v switch {
                "metrics-sqlite" or "metrics_sqlite" => BundleContentKey.MetricsSqlite,
                "call-hierarchy" or "call_hierarchy" => BundleContentKey.CallHierarchy,
                "gc-events" or "gc_events" => BundleContentKey.GcEvents,
                "allocations" => BundleContentKey.Allocations,
                "patches" => BundleContentKey.Patches,
                _ => null,
            };
            if (key is not null) set.Add(key.Value);
        }
        return set;
    }

    private sealed class BundleExportRequestDto {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("include")]
        public string[]? Include { get; set; }

        [JsonPropertyName("force")]
        public bool Force { get; set; }
    }
}
