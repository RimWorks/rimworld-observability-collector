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
using RimWorks.RimObs.Collector.Config;
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
        "frames.json",
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
        names.Should().NotContain(OptionalEntries);
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
                BundleContentKey.Frames,
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
        using StreamReader reader = new StreamReader(await healthEntry.OpenAsync());
        using JsonDocument doc = JsonDocument.Parse(await reader.ReadToEndAsync());

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
        using StreamReader reader = new StreamReader(await manifestEntry.OpenAsync());
        BundleManifest? manifest = JsonSerializer.Deserialize<BundleManifest>(await reader.ReadToEndAsync(), BundleManifest.JsonOptions);

        manifest.Should().NotBeNull();
        manifest!.SessionId.Should().Be("sess-test");
        manifest.SchemaVersion.Should().Be(1);
        manifest.CollectorVersion.Should().Be("0.1.0");
        manifest.Entries.Should().Contain("report.html");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_ManifestListsExactlyWhatTheZipHolds(bool includeOptional) {
        BundleExportService service = new BundleExportService(BuildAggregator(), collectorVersion: "0.1.0");
        HashSet<BundleContentKey> includes = includeOptional
            ? new HashSet<BundleContentKey> {
                BundleContentKey.Allocations,
                BundleContentKey.GcEvents,
                BundleContentKey.Patches,
                BundleContentKey.CallHierarchy,
                BundleContentKey.Frames,
            }
            : new HashSet<BundleContentKey>();

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = includes,
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = zip.GetEntry("manifest.json")!;
        using StreamReader reader = new StreamReader(await manifestEntry.OpenAsync());
        BundleManifest? manifest = JsonSerializer.Deserialize<BundleManifest>(await reader.ReadToEndAsync(), BundleManifest.JsonOptions);

        // manifest.json is the index, not an indexed entry, so it's excluded here.
        manifest!.Entries.Should().BeEquivalentTo(
            zip.Entries.Select(e => e.FullName).Where(n => n != "manifest.json"));
    }

    // the snapshot previews open frames, so the newest frame is in the zip even though no
    // newer ordinal ever arrived to close it.
    [Fact]
    public async Task Export_FramesEntryCarriesTheWholeRing() {
        SessionAggregator aggregator = BuildAggregator();
        for (int ordinal = 1; ordinal <= 2; ordinal++)
            aggregator.Frames.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);
        BundleExportService service = new BundleExportService(aggregator, collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Frames },
            Force = false,
        }, CancellationToken.None);

        result.Status.Should().Be(BundleExportStatus.Ok);
        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using Stream entry = await zip.GetEntry("frames.json")!.OpenAsync();
        using JsonDocument doc = await JsonDocument.ParseAsync(entry);

        JsonElement frames = doc.RootElement.GetProperty("frames");
        frames.GetArrayLength().Should().Be(2);
        frames[0].GetProperty("capture_ordinal").GetInt32().Should().Be(1);
        frames[0].GetProperty("node_count").GetInt32().Should().Be(1);
        frames[0].GetProperty("nodes").GetProperty("section_ids")[0].GetInt32().Should().Be(10);
        doc.RootElement.GetProperty("session_id").GetString().Should().Be("sess-test");
        doc.RootElement.GetProperty("stats").GetProperty("frame_count").GetInt32().Should().Be(2);
    }

    // without the filter set in the file, a re-export of an imported bundle can only stamp the
    // reader's own filter on someone else's frames.
    [Fact]
    public async Task Frames_json_carries_the_filter_that_produced_them() {
        SessionAggregator aggregator = BuildAggregator();
        aggregator.Frames.Add(1, 10, -1, 100, -1, 1000L, 500L);
        AutoInstrumentOptions auto = new AutoInstrumentOptions {
            Enabled = true,
            Filters = "Assembly-CSharp!Verse.*",
            Ignore = "System.*",
        };
        BundleExportService service =
            new BundleExportService(aggregator, collectorVersion: "0.1.0", autoInstrument: () => auto);

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Frames },
            Force = false,
        }, CancellationToken.None);

        result.Status.Should().Be(BundleExportStatus.Ok);
        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using Stream entry = await zip.GetEntry("frames.json")!.OpenAsync();
        using JsonDocument doc = await JsonDocument.ParseAsync(entry);

        JsonElement written = doc.RootElement.GetProperty("auto_instrument");
        written.GetProperty("enabled").GetBoolean().Should().BeTrue();
        written.GetProperty("filters").GetString().Should().Be("Assembly-CSharp!Verse.*");
        written.GetProperty("ignore").GetString().Should().Be("System.*");
    }

    // pressing the export button is a read. lane 7's drain for the exported frames has not
    // landed yet, and it still has to, or a hitch node is lost from the live ring forever.
    [Fact]
    public async Task Export_DoesNotDropTheLanesStillDraining() {
        SessionAggregator aggregator = BuildAggregator();
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            aggregator.Frames.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L, 0L, 1);
        BundleExportService service = new BundleExportService(aggregator, collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Frames },
            Force = false,
        }, CancellationToken.None);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            aggregator.Frames.Add(ordinal, 20, -1, ordinal * 100 + 1, -1, ordinal * 1000L + 100L, 9_000_000L, 0L, 7);

        result.Status.Should().Be(BundleExportStatus.Ok);
        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using Stream entry = await zip.GetEntry("frames.json")!.OpenAsync();
        using JsonDocument doc = await JsonDocument.ParseAsync(entry);
        JsonElement frames = doc.RootElement.GetProperty("frames");
        frames.GetArrayLength().Should().Be(3);
        frames[2].GetProperty("capture_ordinal").GetInt32().Should().Be(3);

        aggregator.Frames.LateSamples.Should().Be(0);
        aggregator.Frames.FindByOrdinal(1)!.ThreadIds.Should().Equal(1, 7);
    }

    [Fact]
    public async Task Estimate_LeavesTheOpenFrameAlone() {
        SessionAggregator aggregator = BuildAggregator();
        aggregator.Frames.Add(1, 10, -1, 100, -1, 1000L, 500L);
        BundleExportService service = new BundleExportService(aggregator, collectorVersion: "0.1.0");

        service.Estimate("sess-test", new HashSet<BundleContentKey> { BundleContentKey.Frames })
            .Status.Should().Be(BundleExportStatus.Ok);
        aggregator.Frames.Add(1, 11, -1, 101, -1, 1100L, 500L);
        aggregator.Frames.LateSamples.Should().Be(0);

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Frames },
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using Stream entry = await zip.GetEntry("frames.json")!.OpenAsync();
        using JsonDocument doc = await JsonDocument.ParseAsync(entry);

        JsonElement frames = doc.RootElement.GetProperty("frames");
        frames.GetArrayLength().Should().Be(1);
        frames[0].GetProperty("node_count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Export_FramesEntryCarriesTheStopwatchFrequency() {
        SessionAggregator aggregator = BuildAggregator();
        aggregator.Frames.Add(1, 10, -1, 100, -1, 1000L, 500L);
        aggregator.Frames.Add(2, 10, -1, 200, -1, 2000L, 500L);
        BundleExportService service = new BundleExportService(aggregator, collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Frames },
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using Stream entry = await zip.GetEntry("frames.json")!.OpenAsync();
        using JsonDocument doc = await JsonDocument.ParseAsync(entry);

        doc.RootElement.GetProperty("stopwatch_frequency").GetInt64().Should().Be(10_000_000L);
    }

    [Fact]
    public async Task Export_FramesEntryIsWrittenCompact() {
        SessionAggregator aggregator = BuildAggregator();
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            aggregator.Frames.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);
        BundleExportService service = new BundleExportService(aggregator, collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Frames },
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry jsonEntry in zip.Entries.Where(e => e.FullName.EndsWith(".json"))) {
            using StreamReader reader = new StreamReader(await jsonEntry.OpenAsync());
            string text = await reader.ReadToEndAsync();
            if (jsonEntry.FullName == "frames.json")
                text.Should().NotContain("\n");
            else
                text.Should().Contain("\n");
        }
    }

    [Fact]
    public async Task Export_HotspotsCarryTheSubsystem() {
        SessionAggregator aggregator = BuildAggregator();
        aggregator.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [10],
            Names = ["Verse.TickList.Tick"],
            Subsystems = ["tick"],
        });
        BundleExportService service = new BundleExportService(aggregator, collectorVersion: "0.1.0");

        BundleExportResult result = await service.ExportAsync(new BundleExportRequest {
            SessionId = "sess-test",
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);

        using MemoryStream ms = new MemoryStream(result.Bytes!);
        using ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using Stream entry = await zip.GetEntry("hotspots.json")!.OpenAsync();
        using JsonDocument doc = await JsonDocument.ParseAsync(entry);

        doc.RootElement.GetProperty("hotspots")[0]
            .GetProperty("subsystem").GetString().Should().Be("tick");
    }
}
