using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class VersionEndpoints {
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/version", () => Results.Ok(new {
            schema_version = SchemaVersion.Current,
            version = BuildInfo.Revision,
            built_at = BuildInfo.BuildTime,
        }));

        return endpoints;
    }
}
