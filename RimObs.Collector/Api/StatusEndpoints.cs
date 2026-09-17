using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class StatusEndpoints {
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/status", (
            SessionAggregator aggregator,
            Update.UpdateState updateState) =>
                Results.Ok(BuildStatusPayload(aggregator, updateState)));

        return endpoints;
    }

    /// <summary>Shared by the endpoint and the SSE slow lane, so the shapes cannot drift.</summary>
    public static object BuildStatusPayload(
        SessionAggregator aggregator,
        Update.UpdateState updateState) {
        SessionMeta? meta = aggregator.Meta;
        Update.ReleaseInfo? latest = updateState.Latest;
        return new {
            schema_version = SchemaVersion.Current,
            status = "running",
            version = BuildInfo.Revision,
            session = meta is null
                        ? null
                        : SessionsEndpoints.MapSession(meta, isCurrent: true, aggregator.SessionName),
            receive = ReceiveCounters.Project(aggregator),
            update = new {
                available = latest is not null,
                latest_version = latest?.TagName,
                url = latest?.HtmlUrl,
            },
        };
    }
}
