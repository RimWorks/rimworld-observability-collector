using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Exporters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace RimWorks.RimObs.Collector.Api;

public static class MetricsExporterEndpoints {
    private const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    public static IEndpointRouteBuilder MapMetricsExporterEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/metrics", (
            ConfigStore configStore,
            SessionAggregator aggregator,
            PrometheusMetricsBuilder builder,
            ExporterHealth health,
            ILoggerFactory loggerFactory) => {
                if (!configStore.Current.Exporters.PrometheusEnabled) {
                    return Results.NotFound();
                }

                try {
                    PrometheusRender render = builder.Render(aggregator);
                    health.RecordScrape(render.SampleCount);
                    return Results.Text(render.Body, ContentType);
                }
                catch (System.Exception ex) {
                    // Exporter failure must not affect core behavior (PRD §17.1.4):
                    // log, record health, and return 503 without propagating.
                    health.RecordError(ex.Message);
                    loggerFactory.CreateLogger("PrometheusExporter").LogWarning(ex, "Prometheus scrape failed");
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
            });

        return endpoints;
    }
}
