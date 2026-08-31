using RimWorks.RimObs.Collector.Logging;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog.Events;

namespace RimWorks.RimObs.Collector.Api;

public static class LogsEndpoints {
    public const int DefaultLimit = 200;
    public const int MaxLimit = 1024;

    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/api/v1/logs", (RingBufferLogSink sink, string? level, int? limit) => {
            if (!string.IsNullOrWhiteSpace(level) && !Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out _)) {
                return Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = $"unknown level '{level}'" });
            }
            int effective = QueryLimit.Clamp(limit, DefaultLimit, MaxLimit);
            IReadOnlyList<LogEntry> entries = sink.Snapshot(level, effective);
            return Results.Ok(new {
                schema_version = SchemaVersion.Current,
                count = entries.Count,
                entries = entries.Select(e => new {
                    timestamp = e.Timestamp,
                    level = e.Level.ToString(),
                    message = e.Message,
                    exception = e.Exception,
                }),
            });
        });

        return endpoints;
    }
}
