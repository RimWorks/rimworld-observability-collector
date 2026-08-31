using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimWorks.RimObs.Collector.Bundle;

public sealed class BundleManifest {
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTime CreatedUtc { get; set; }

    [JsonPropertyName("collector_version")]
    public string CollectorVersion { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public string[] Entries { get; set; } = Array.Empty<string>();
}
