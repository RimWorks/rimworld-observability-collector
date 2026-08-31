using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Bundle;
using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class ComparisonEndpointsTests : IDisposable {
    private readonly string _sessionsDir;

    public ComparisonEndpointsTests() {
        _sessionsDir = Path.Combine(Path.GetTempPath(), "rimobs-compare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionsDir);
    }

    public void Dispose() {
        try {
            Directory.Delete(_sessionsDir, recursive: true);
        }
        catch (IOException) {
        }
        GC.SuppressFinalize(this);
    }

    private void SeedSession(string sessionId, string gameVersion, params (int Id, string Name, long Samples, long Total)[] sections) {
        using SqliteSessionPersister persister = new(_sessionsDir);
        persister.WriteSessionMeta(new SessionMeta {
            SessionId = sessionId,
            StartedUtcTicks = DateTime.UtcNow.Ticks,
            StopwatchFrequency = 1_000_000_000,
            AnchorTimestamp = 0,
            LibraryVersion = "1.0.0",
            GameVersion = gameVersion,
        });

        SectionStats[] stats = new SectionStats[sections.Length];
        for (int i = 0; i < sections.Length; i++) {
            stats[i] = new SectionStats {
                SectionId = sections[i].Id,
                Name = sections[i].Name,
                SampleCount = sections[i].Samples,
                TotalElapsedTicks = sections[i].Total,
                MinElapsedTicks = 1,
                MaxElapsedTicks = sections[i].Total,
                LastStartTimestamp = 0,
            };
        }
        persister.WriteSectionsSnapshot(sessionId, stats);
    }

    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task WithApp(Func<HttpClient, Task> body) {
        await WithApp((http, _) => body(http));
    }

    private async Task WithApp(Func<HttpClient, WebApplication, Task> body) {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port, sessionsDir: _sessionsDir);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await body(http, app);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static byte[] BuildBundleZip(string sessionId, params (int Id, string Name, long ElapsedTicks)[] sections) {
        SessionAggregator aggregator = new SessionAggregator();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = sessionId,
            StartedUtcTicks = DateTime.UtcNow.Ticks,
            StopwatchFrequency = 10_000_000,
            LibraryVersion = "1.0.0",
            GameVersion = "1.5",
        });
        int[] ids = new int[sections.Length];
        string[] names = new string[sections.Length];
        long[] starts = new long[sections.Length];
        long[] elapsed = new long[sections.Length];
        for (int i = 0; i < sections.Length; i++) {
            ids[i] = sections[i].Id;
            names[i] = sections[i].Name;
            starts[i] = (i + 1) * 100;
            elapsed[i] = sections[i].ElapsedTicks;
        }
        aggregator.OnSectionRegistrations(new SectionRegistrationsBatch { SectionIds = ids, Names = names });
        aggregator.OnSectionBatch(new SectionBatch { SectionIds = ids, StartTimestamps = starts, ElapsedTicks = elapsed });

        BundleExportService export = new BundleExportService(aggregator, collectorVersion: "0.1.0");
        BundleExportResult result = export.ExportAsync(new BundleExportRequest {
            SessionId = sessionId,
            Includes = new HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None).GetAwaiter().GetResult();
        result.Status.Should().Be(BundleExportStatus.Ok);
        return result.Bytes!;
    }

    [Fact]
    public async Task Persisted_session_summary_reports_section_count_and_total() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 1000), (2, "modB_draw", 5, 500));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/base/summary");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            root.GetProperty("section_count").GetInt32().Should().Be(2);
            root.GetProperty("total_section_ns").GetInt64().Should().Be(1500);
        });
    }

    [Fact]
    public async Task Persisted_session_hotspots_are_sorted_by_total() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 1000), (2, "modB_draw", 5, 5000));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/base/hotspots");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement hotspots = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("hotspots");
            hotspots[0].GetProperty("name").GetString().Should().Be("modB_draw");
        });
    }

    [Fact]
    public async Task Load_order_groups_owners_by_prefix() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 1000), (2, "modA_tick", 5, 500), (3, "modB_draw", 5, 200));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/base/load_order");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement owners = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("owners");
            owners.GetArrayLength().Should().Be(2);
            owners[0].GetProperty("owner").GetString().Should().Be("modA");
            owners[0].GetProperty("total_ns").GetInt64().Should().Be(1500);
        });
    }

    [Fact]
    public async Task Unknown_session_returns_404() {
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/does-not-exist/summary");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task Compare_requires_both_base_and_head() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 1000));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/compare?base=base");
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task Compare_404_when_head_unknown() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 1000));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/compare?base=base&head=missing");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task Compare_returns_timing_delta_and_hedged_disclaimer() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 1000));
        SeedSession("head", "1.5", (1, "modA_scan", 10, 1500));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/compare?base=base&head=head");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            root.GetProperty("timing").GetProperty("delta_ns").GetInt64().Should().Be(500);
            root.GetProperty("disclaimer").GetString().Should().Contain("not causation");
        });
    }

    [Fact]
    public async Task Compare_flags_regression_candidate_and_load_order_changes() {
        SeedSession("base", "1.5", (1, "modA_scan", 10, 4_000_000), (2, "modB_x", 1, 10));
        SeedSession("head", "1.5", (1, "modA_scan", 10, 9_000_000), (3, "modC_y", 1, 10));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/compare?base=base&head=head");
            JsonElement root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

            bool anyCandidate = false;
            foreach (JsonElement h in root.GetProperty("hotspots").EnumerateArray()) {
                if (h.GetProperty("name").GetString() == "modA_scan")
                    anyCandidate = h.GetProperty("likely_regression_candidate").GetBoolean();
            }
            anyCandidate.Should().BeTrue();

            root.GetProperty("load_order").GetProperty("added")[0].GetString().Should().Be("modC");
            root.GetProperty("load_order").GetProperty("removed")[0].GetString().Should().Be("modB");
        });
    }

    [Fact]
    public async Task Compare_accepts_imported_bundle_as_base_source() {
        byte[] zip = BuildBundleZip("bundle-base", (1, "modA_scan", 10));
        SeedSession("head", "1.5", (1, "modA_scan", 10, 1500));
        await WithApp(async (http, app) => {
            BundleImportService importer = app.Services.GetRequiredService<BundleImportService>();
            using MemoryStream archive = new MemoryStream(zip);
            BundleImportResult import = await importer.ImportAsync(archive);
            import.Status.Should().Be(BundleImportStatus.Ok);

            HttpResponseMessage res = await http.GetAsync($"/api/v1/sessions/compare?base=bundle:{import.Entry!.Token}&head=head");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
            root.GetProperty("timing").GetProperty("base_total_ns").GetInt64().Should().Be(1000);
            root.GetProperty("timing").GetProperty("head_total_ns").GetInt64().Should().Be(1500);
            root.GetProperty("timing").GetProperty("delta_ns").GetInt64().Should().Be(500);
        });
    }

    [Fact]
    public async Task Compare_supports_bundle_versus_bundle() {
        byte[] baseZip = BuildBundleZip("bundle-base", (1, "modA_scan", 10));
        byte[] headZip = BuildBundleZip("bundle-head", (1, "modA_scan", 30));
        await WithApp(async (http, app) => {
            BundleImportService importer = app.Services.GetRequiredService<BundleImportService>();
            using MemoryStream baseArchive = new MemoryStream(baseZip);
            BundleImportResult baseImport = await importer.ImportAsync(baseArchive);
            using MemoryStream headArchive = new MemoryStream(headZip);
            BundleImportResult headImport = await importer.ImportAsync(headArchive);

            HttpResponseMessage res = await http.GetAsync(
                $"/api/v1/sessions/compare?base=bundle:{baseImport.Entry!.Token}&head=bundle:{headImport.Entry!.Token}");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement timing = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("timing");
            timing.GetProperty("base_total_ns").GetInt64().Should().Be(1000);
            timing.GetProperty("head_total_ns").GetInt64().Should().Be(3000);
            timing.GetProperty("delta_ns").GetInt64().Should().Be(2000);
        });
    }

    [Fact]
    public async Task Compare_404_when_bundle_token_unknown() {
        SeedSession("head", "1.5", (1, "modA_scan", 10, 1500));
        await WithApp(async http => {
            HttpResponseMessage res = await http.GetAsync("/api/v1/sessions/compare?base=bundle:deadbeef&head=head");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        });
    }
}
