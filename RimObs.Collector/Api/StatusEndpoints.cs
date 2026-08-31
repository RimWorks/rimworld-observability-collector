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
            Update.UpdateState updateState,
            Config.ConfigStore configStore,
            Exporters.ExporterHealth exporterHealth) => {
                SessionMeta? meta = aggregator.Meta;
                Update.ReleaseInfo? latest = updateState.Latest;
                Config.ExporterOptions exporters = configStore.Current.Exporters;
                return Results.Ok(new {
                    schema_version = SchemaVersion.Current,
                    status = "running",
                    version = BuildInfo.Revision,
                    session = meta is null ? null : SessionsEndpoints.MapSession(meta, isCurrent: true),
                    receive = ReceiveCounters.Project(aggregator),
                    update = new {
                        available = latest is not null,
                        latest_version = latest?.TagName,
                        url = latest?.HtmlUrl,
                    },
                    exporters = new {
                        prometheus_enabled = exporters.PrometheusEnabled,
                        prometheus_port = exporters.PrometheusPort,
                        otlp_enabled = exporters.OtlpEnabled,
                        prometheus_health = new {
                            total_scrapes = exporterHealth.TotalScrapes,
                            last_scrape_utc = exporterHealth.LastScrapeUtc,
                            last_sample_count = exporterHealth.LastSampleCount,
                            total_errors = exporterHealth.TotalErrors,
                            last_error = exporterHealth.LastError,
                        },
                    },
                });
            });

        return endpoints;
    }
}
