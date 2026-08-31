using System;
using System.Text.Json;
using RimWorks.RimObs.Collector.Bundle;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleManifestTests {
    private static readonly string[] ExpectedEntries = ["manifest.json", "session_summary.json"];
    [Fact]
    public void Manifest_RoundTripsThroughJson() {
        BundleManifest manifest = new BundleManifest {
            SchemaVersion = 1,
            SessionId = "sess-abc",
            CreatedUtc = new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc),
            CollectorVersion = "0.1.0",
            Entries = new[] { "manifest.json", "session_summary.json" },
        };

        string json = JsonSerializer.Serialize(manifest, BundleManifest.JsonOptions);
        BundleManifest? round = JsonSerializer.Deserialize<BundleManifest>(json, BundleManifest.JsonOptions);

        round.Should().NotBeNull();
        round!.SchemaVersion.Should().Be(1);
        round.SessionId.Should().Be("sess-abc");
        round.Entries.Should().BeEquivalentTo(ExpectedEntries);
    }

    [Fact]
    public void Manifest_UsesSnakeCaseJsonNames() {
        BundleManifest manifest = new BundleManifest {
            SchemaVersion = 1,
            SessionId = "s",
            CreatedUtc = DateTime.UtcNow,
            CollectorVersion = "v",
            Entries = Array.Empty<string>(),
        };

        string json = JsonSerializer.Serialize(manifest, BundleManifest.JsonOptions);

        json.Should().Contain("\"schema_version\"");
        json.Should().Contain("\"session_id\"");
        json.Should().Contain("\"created_utc\"");
        json.Should().Contain("\"collector_version\"");
    }

    [Fact]
    public void ContentKey_HasExpectedEntries() {
        Enum.GetValues<BundleContentKey>().Should().BeEquivalentTo(new[] {
            BundleContentKey.MetricsSqlite,
            BundleContentKey.CallHierarchy,
            BundleContentKey.GcEvents,
            BundleContentKey.Allocations,
            BundleContentKey.Patches,
        });
    }
}
