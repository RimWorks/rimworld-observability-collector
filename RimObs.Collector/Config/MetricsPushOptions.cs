using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimWorks.RimObs.Collector.Config;

public sealed class MetricsPushOptions {
    public const int DefaultIntervalSeconds = 10;
    public const int MinIntervalSeconds = 1;
    public const int MaxIntervalSeconds = 300;

    // what the api sends back in place of a set bearer_token, and what it accepts as "keep it".
    public const string RedactedToken = "__redacted__";

    public bool Enabled { get; set; }

    // prometheus remote-write url, like http://mimir:9009/api/v1/push
    public string Endpoint { get; set; } = string.Empty;

    // lives in config.json in the clear. GET /api/v1/config is unauthenticated and LAN-reachable,
    // so the api masks it; only disk sees the real value.
    public string BearerToken { get; set; } = string.Empty;

    // mimir and GEM run multitenant by default and reject a push without it, with a 401 "no org id".
    // sent as X-Scope-OrgID. not a secret, so the api hands it back in the clear.
    public string TenantId { get; set; } = string.Empty;

    // "instanceID:token" for grafana cloud, which wants basic auth instead of a bearer. wins over
    // BearerToken when both are set. masked by the api the same way the tokens are.
    public string BasicAuth { get; set; } = string.Empty;

    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    // grafana's base url, like http://grafana:3000. set it to get a region annotation per session.
    public string GrafanaUrl { get; set; } = string.Empty;

    // a grafana service-account token. masked by the api the same way BearerToken is.
    public string GrafanaToken { get; set; } = string.Empty;

    // pyroscope's base url, like https://pyroscope.example.com. set it to push call-tree profiles.
    public string ProfileEndpoint { get; set; } = string.Empty;

    // "user:password" for pyroscope. masked by the api the same way the tokens are.
    public string ProfileBasicAuth { get; set; } = string.Empty;

    // merged into every pushed series, so a colony can be tagged per host or per run.
    public Dictionary<string, string> ExtraLabels { get; set; } = [];

    public MetricsPushOptions WithRedactedToken() => new() {
        Enabled = Enabled,
        Endpoint = Endpoint,
        BearerToken = string.IsNullOrEmpty(BearerToken) ? string.Empty : RedactedToken,
        TenantId = TenantId,
        BasicAuth = string.IsNullOrEmpty(BasicAuth) ? string.Empty : RedactedToken,
        IntervalSeconds = IntervalSeconds,
        GrafanaUrl = GrafanaUrl,
        GrafanaToken = string.IsNullOrEmpty(GrafanaToken) ? string.Empty : RedactedToken,
        ProfileEndpoint = ProfileEndpoint,
        ProfileBasicAuth = string.IsNullOrEmpty(ProfileBasicAuth) ? string.Empty : RedactedToken,
        ExtraLabels = ExtraLabels,
    };

    public static int ClampIntervalSeconds(int value) {
        int floored = value < MinIntervalSeconds ? MinIntervalSeconds : value;
        return floored > MaxIntervalSeconds ? MaxIntervalSeconds : floored;
    }
}

/// <summary>Masks the inline bearer token on the way out. Only <see cref="ConfigJson.PublicOptions"/>
/// carries it, so the copy on disk keeps the real value.</summary>
public sealed class RedactedMetricsPushConverter : JsonConverter<MetricsPushOptions> {
    public override MetricsPushOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<MetricsPushOptions>(ref reader, ConfigJson.Options);

    public override void Write(Utf8JsonWriter writer, MetricsPushOptions value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.WithRedactedToken(), ConfigJson.Options);
}
