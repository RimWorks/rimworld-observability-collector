using System.Text.Json;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Wire;
using Microsoft.AspNetCore.Http;

namespace RimWorks.RimObs.Collector.Api;

public static class RequestBody {
    // Response envelopes always carry the wire SchemaVersion.Current. The domain version
    // (config/panel registration schema) is only used to validate the inbound body.
    // Config/panel bodies are snake_case, so this path uses ConfigJson.Options.
    public static async Task<(T? Body, IResult? Error)> ReadValidated<T>(
        HttpContext context,
        int domainVersion,
        Func<T, int> getSchemaVersion,
        string entity)
        where T : class {
        (T? incoming, IResult? error) = await ReadBody<T>(context, entity, ConfigJson.Options);
        if (error is not null)
            return (null, error);

        int incomingVersion = getSchemaVersion(incoming!);
        if (incomingVersion != domainVersion) {
            return (null, Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = $"unsupported schema_version {incomingVersion}" }));
        }

        return (incoming, null);
    }

    // For bodies that carry no schema_version of their own. Produces the same
    // malformed/empty-body envelope as ReadValidated so every POST handler reports
    // body errors identically. Uses the host's default JSON options (camelCase) so
    // it matches the wire contract these endpoints already accept.
    public static Task<(T? Body, IResult? Error)> Read<T>(HttpContext context, string entity)
        where T : class =>
        ReadBody<T>(context, entity, null);

    private static async Task<(T? Body, IResult? Error)> ReadBody<T>(
        HttpContext context,
        string entity,
        JsonSerializerOptions? options)
        where T : class {
        T? incoming;
        try {
            incoming = options is null
                ? await context.Request.ReadFromJsonAsync<T>(context.RequestAborted)
                : await context.Request.ReadFromJsonAsync<T>(options, context.RequestAborted);
        }
        catch (JsonException) {
            return (null, Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = $"malformed {entity} body" }));
        }

        if (incoming is null) {
            return (null, Results.BadRequest(new { schema_version = SchemaVersion.Current, reason = $"empty {entity} body" }));
        }

        return (incoming, null);
    }
}
