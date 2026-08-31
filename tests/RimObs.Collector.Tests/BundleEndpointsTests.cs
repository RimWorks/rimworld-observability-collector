using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Security;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class BundleEndpointsTests {
    private readonly ITestOutputHelper _out;

    public BundleEndpointsTests(ITestOutputHelper output) {
        _out = output;
    }

    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task BuildApp_disposes_dynamic_patch_store_so_db_file_unlocks() {
        // Regression: the DynamicPatchStore was registered as a pre-built singleton
        // instance, which the DI container never disposes. Its SqliteConnection stayed
        // open, and on Windows that kept dynamic_patches.db locked, so test cleanup
        // (Directory.Delete) threw IOException. The fix registers it via a factory so the
        // container owns and disposes it. Linux unlinks open files silently, so the teeth
        // here are the disposal assertion, not the delete.
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("dispose-bearer-token");
        string tempDir = Path.Combine(Path.GetTempPath(), "rimobs-dispose-" + Guid.NewGuid().ToString("N"));
        string sessionsDir = Path.Combine(tempDir, "sessions");
        Directory.CreateDirectory(sessionsDir);
        try {
            WebApplication app = Program.BuildApp([], port, token, sessionsDir);
            DynamicPatchStore store = app.Services.GetRequiredService<DynamicPatchStore>();
            await app.DisposeAsync();

            store.Invoking(s => s.List())
                .Should().Throw<InvalidOperationException>(
                    "the container must own and dispose the store on shutdown, closing the "
                    + "SQLite connection that otherwise keeps dynamic_patches.db locked on Windows");

            Directory.Delete(tempDir, recursive: true);
        }
        finally {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Export_current_session_returns_zip_with_manifest_and_report() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("bundle-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
            SessionMeta meta = new SessionMeta {
                SessionId = "bundle-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-bundle",
                GameVersion = "1.6",
            };
            aggregator.OnSessionMeta(meta);

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/export/bundle") {
                Content = new StringContent(
                    "{\"session_id\":\"bundle-session\",\"include\":[],\"force\":false}",
                    Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage resp = await http.SendAsync(post);
            _out.WriteLine($"export status: {resp.StatusCode}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            resp.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
            resp.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".rimobs.zip");
            resp.Content.Headers.ContentDisposition!.FileName.Should().Contain("bundle-session");

            byte[] zipBytes = await resp.Content.ReadAsByteArrayAsync();
            zipBytes.Length.Should().BeGreaterThan(0);

            using MemoryStream ms = new MemoryStream(zipBytes);
            using ZipArchive archive = new ZipArchive(ms, ZipArchiveMode.Read);
            HashSet<string> names = archive.Entries.Select(e => e.FullName).ToHashSet();
            names.Should().Contain("manifest.json");
            names.Should().Contain("report.html");
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Export_unknown_session_returns_404() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("bundle-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
            aggregator.OnSessionMeta(new SessionMeta {
                SessionId = "real-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-bundle",
                GameVersion = "1.6",
            });

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/export/bundle") {
                Content = new StringContent(
                    "{\"session_id\":\"nope\",\"include\":[],\"force\":false}",
                    Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage resp = await http.SendAsync(post);
            _out.WriteLine($"export status: {resp.StatusCode}");
            resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Export_malformed_body_returns_400_with_shared_envelope() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("bundle-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/export/bundle") {
                Content = new StringContent("{ not valid json", Encoding.UTF8, "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage resp = await http.SendAsync(post);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "a malformed body must be rejected by the shared RequestBody reader, not surface as a 500");
            JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("reason").GetString().Should().Be("malformed bundle export body");
            body.GetProperty("schema_version").GetInt32().Should().Be(SchemaVersion.Current);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Estimate_current_session_returns_size_and_cap() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("bundle-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
            aggregator.OnSessionMeta(new SessionMeta {
                SessionId = "estimate-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-bundle",
                GameVersion = "1.6",
            });

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/export/bundle/estimate") {
                Content = new StringContent(
                    "{\"session_id\":\"estimate-session\",\"include\":[],\"force\":false}",
                    Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage resp = await http.SendAsync(post);
            _out.WriteLine($"estimate status: {resp.StatusCode}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("estimated_bytes").GetInt64().Should().BeGreaterThan(0);
            body.GetProperty("cap_bytes").GetInt64().Should().BeGreaterThan(0);
            body.GetProperty("exceeds_soft_cap").GetBoolean().Should().BeFalse();
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Estimate_unknown_session_returns_404() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("bundle-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
            aggregator.OnSessionMeta(new SessionMeta {
                SessionId = "real-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-bundle",
                GameVersion = "1.6",
            });

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/export/bundle/estimate") {
                Content = new StringContent(
                    "{\"session_id\":\"nope\",\"include\":[],\"force\":false}",
                    Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage resp = await http.SendAsync(post);
            _out.WriteLine($"estimate status: {resp.StatusCode}");
            resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    [Fact]
    public async Task Import_roundtrip_exposes_files_by_token() {
        int port = PickFreePort();
        string tempDir = Path.Combine(Path.GetTempPath(), $"rimobs-import-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sessionsDir = Path.Combine(tempDir, "sessions");
        Directory.CreateDirectory(sessionsDir);

        try {
            CollectorToken token = CollectorToken.FromExplicitValue("bundle-bearer-token");
            WebApplication app = Program.BuildApp([], port, token, sessionsDir);
            await app.StartAsync();
            try {
                using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
                await WaitFor(async () => {
                    HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                    return r.IsSuccessStatusCode;
                }, TimeSpan.FromSeconds(3));

                SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
                aggregator.OnSessionMeta(new SessionMeta {
                    SessionId = "rt-session",
                    StartedUtcTicks = DateTime.UtcNow.Ticks,
                    StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                    AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                    LibraryVersion = "0.0.0-bundle",
                    GameVersion = "1.6",
                });

                using HttpRequestMessage exportRequest = new(HttpMethod.Post, "/api/v1/export/bundle") {
                    Content = new StringContent(
                        "{\"session_id\":\"rt-session\",\"include\":[],\"force\":false}",
                        Encoding.UTF8,
                        "application/json"),
                };
                exportRequest.Headers.Add("Origin", $"http://127.0.0.1:{port}");
                exportRequest.Headers.Add("Authorization", $"Bearer {token.Value}");
                HttpResponseMessage exportResponse = await http.SendAsync(exportRequest);
                exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                byte[] zipBytes = await exportResponse.Content.ReadAsByteArrayAsync();
                zipBytes.Length.Should().BeGreaterThan(0);

                using ByteArrayContent fileContent = new ByteArrayContent(zipBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                using MultipartFormDataContent form = new MultipartFormDataContent {
                    { fileContent, "bundle", "test.rimobs.zip" },
                };
                using HttpRequestMessage importRequest = new(HttpMethod.Post, "/api/v1/import/bundle") {
                    Content = form,
                };
                importRequest.Headers.Add("Origin", $"http://127.0.0.1:{port}");
                importRequest.Headers.Add("Authorization", $"Bearer {token.Value}");
                HttpResponseMessage importResponse = await http.SendAsync(importRequest);
                _out.WriteLine($"import status: {importResponse.StatusCode}");
                importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                JsonElement body = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
                string importToken = body.GetProperty("token").GetString()!;
                importToken.Should().NotBeNullOrEmpty();

                using HttpRequestMessage fileRequest = new(HttpMethod.Get, $"/api/v1/import/bundle/{importToken}/file/report.html");
                fileRequest.Headers.Add("Origin", $"http://127.0.0.1:{port}");
                fileRequest.Headers.Add("Authorization", $"Bearer {token.Value}");
                HttpResponseMessage fileResponse = await http.SendAsync(fileRequest);
                fileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"/api/v1/import/bundle/{importToken}");
                deleteRequest.Headers.Add("Origin", $"http://127.0.0.1:{port}");
                deleteRequest.Headers.Add("Authorization", $"Bearer {token.Value}");
                HttpResponseMessage deleteResponse = await http.SendAsync(deleteRequest);
                deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

                using HttpRequestMessage afterDeleteRequest = new(HttpMethod.Get, $"/api/v1/import/bundle/{importToken}/file/report.html");
                afterDeleteRequest.Headers.Add("Origin", $"http://127.0.0.1:{port}");
                afterDeleteRequest.Headers.Add("Authorization", $"Bearer {token.Value}");
                HttpResponseMessage afterDeleteResponse = await http.SendAsync(afterDeleteRequest);
                afterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            }
            finally {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task WaitFor(Func<Task<bool>> predicate, TimeSpan timeout) {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (await predicate())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Predicate not satisfied within {timeout}.");
    }
}
