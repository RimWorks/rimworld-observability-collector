using System.Text.Json;

namespace RimWorks.RimObs.Collector.Config;

public static class ConfigJson {
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>What the HTTP api serializes with: same shape, secrets masked.</summary>
    public static readonly JsonSerializerOptions PublicOptions = new(Options) {
        Converters = { new RedactedMetricsPushConverter() },
    };
}
