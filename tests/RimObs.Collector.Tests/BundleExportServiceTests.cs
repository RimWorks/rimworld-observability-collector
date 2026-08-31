using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Bundle;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleExportServiceTests {
    private static readonly string[] RequiredEntries = [
        "manifest.json",
        "session_summary.json",
        "metric_descriptors.json",
        "hotspots.json",
        "custom_metrics.json",
        "load_order.json",
        "collector_health.json",
        "report.html",
    ];
    private static readonly string[] OptionalEntries = [
        "allocations.json",
        "gc_events.json",
        "patches.json",
        "call_hierarchy.json",
    ];
    private static SessionAggregator BuildAggregator() {
        SessionAggregator aggregator = new SessionAggregator();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "sess-test",
            StartedUtcTicks = new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc).Ticks,
            StopwatchFrequency = 10_000_000,
            LibraryVersion = "0.1.0",
            GameVersion = "1.5",
        });
        return aggregator;
    }

    [Fact]
    public async Task Export_WritesAllRequiredEntries() {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);

        result.Status.Should().Be(BundleExportStatus.Ok);
        result.Bytes.Should().NotBeNull();

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        IEnumerable<string> names = zip.Entries.Select(e => e.FullName);
        names.Should().Contain(RequiredEntries);
    }

    [Fact]
    public async Task Export_OptionalEntriesAddedWhenIncluded() {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> {
                BundleContentKey.Allocations,
                BundleContentKey.GcEvents,
                BundleContentKey.Patches,
                BundleContentKey.CallHierarchy,
            },
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        IEnumerable<string> names = zip.Entries.Select(e => e.FullName).ToArray();
        names.Should().Contain(OptionalEntries);
        names.Should().NotContain("metrics.sqlite");
    }

    [Fact]
    public async Task Export_RejectsUnknownSession() {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "wrong-id",
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);

        result.Status.Should().Be(BundleExportStatus.UnknownSession);
        result.Bytes.Should().BeNull();
    }

    [Fact]
    public async Task Export_RejectsOverCapWithoutForce() {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0") {
            EstimateOverride = _ => new BundleSizeEstimate(BundleSizeEstimator.SoftCapBytes + 1),
        };

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);

        result.Status.Should().Be(BundleExportStatus.ExceedsSoftCap);
        result.EstimatedBytes.Should().BeGreaterThan(BundleSizeEstimator.SoftCapBytes);
    }

    [Fact]
    public async Task Export_OverCapWithForce_Succeeds() {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0") {
            EstimateOverride = _ => new BundleSizeEstimate(BundleSizeEstimator.SoftCapBytes + 1),
        };

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey>(),
            Force = true,
        }, CancellationToken.None);

        result.Status.Should().Be(BundleExportStatus.Ok);
    }

    [Fact]
    public async Task Export_CollectorHealthReportsRealUptime() {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(120);
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0", startedUtc: startedUtc);

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        ZipArchiveEntry healthEntry = zip.GetEntry("collector_health.json")!;
        using StreamReader reader = new StreamReader(healthEntry.Open());
        using JsonDocument doc = JsonDocument.Parse(reader.ReadToEnd());

        double uptime = doc.RootElement.GetProperty("uptime_seconds").GetDouble();
        uptime.Should().BeGreaterThanOrEqualTo(120, "uptime is now - collector start, not the always-zero a - a regression (S1764)");
        uptime.Should().BeLessThan(600, "elapsed should track the injected 120s start, not run away");
    }

    [Fact]
    public async Task Export_ManifestLooksWellFormed() {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0");
        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = zip.GetEntry("manifest.json")!;
        using StreamReader reader = new StreamReader(manifestEntry.Open());
        BundleManifest? manifest = JsonSerializer.Deserialize<BundleManifest>(reader.ReadToEnd(), BundleManifest.JsonOptions);

        manifest.Should().NotBeNull();
        manifest!.SessionId.Should().Be("sess-test");
        manifest.SchemaVersion.Should().Be(1);
        manifest.CollectorVersion.Should().Be("0.1.0");
        manifest.Entries.Should().Contain("report.html");
    }
}
