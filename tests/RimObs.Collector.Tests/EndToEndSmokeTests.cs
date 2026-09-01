using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Tests.Stubs;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class EndToEndSmokeTests {
    private readonly ITestOutputHelper _out;

    public EndToEndSmokeTests(ITestOutputHelper output) {
        _out = output;
    }

    // asks the OS for a free port then releases it. the TOCTOU window is sub-millisecond on
    // loopback and these tests run serially, so collisions have not been seen.
    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Udp_batch_appears_in_status_and_sections() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "smoke-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.SectionRegistrations, WireCodec.Serialize(new SectionRegistrationsBatch {
                SectionIds = [42, 43],
                Names = ["smoke.tick", "smoke.path"],
            }));

            SendBatch(port, BatchType.Sections, WireCodec.Serialize(new SectionBatch {
                SectionIds = [42, 42, 43],
                StartTimestamps = [1000, 2000, 3000],
                ElapsedTicks = [500, 600, 700],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/status");
                _out.WriteLine($"status: {body}");
                using JsonDocument doc = JsonDocument.Parse(body);
                int samples = doc.RootElement.GetProperty("receive").GetProperty("total_samples").GetInt32();
                return samples >= 3;
            }, TimeSpan.FromSeconds(3));

            string statusBody = await http.GetStringAsync("/api/v1/status");
            using JsonDocument status = JsonDocument.Parse(statusBody);
            status.RootElement.GetProperty("receive").GetProperty("total_samples").GetInt32().Should().BeGreaterThanOrEqualTo(3);
            status.RootElement.GetProperty("receive").GetProperty("section_count").GetInt32().Should().Be(2);
            status.RootElement.GetProperty("session").GetProperty("id").GetString().Should().Be("smoke-session");

            string sectionsBody = await http.GetStringAsync("/api/v1/sessions/current/sections");
            _out.WriteLine($"sections: {sectionsBody}");
            using JsonDocument sectionsDoc = JsonDocument.Parse(sectionsBody);
            JsonElement sections = sectionsDoc.RootElement.GetProperty("sections");
            sections.GetArrayLength().Should().Be(2);

            int totalSamples = 0;
            foreach (JsonElement s in sections.EnumerateArray())
                totalSamples += s.GetProperty("sample_count").GetInt32();
            totalSamples.Should().Be(3);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    [Fact]
    public async Task GcEvents_and_Allocations_batches_increment_status_counters() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.GcEvents, WireCodec.Serialize(new GcEventsBatch {
                Generations = [0, 1, 2],
                PauseTypes = [0, 0, 1],
                HeapBefore = [100, 200, 300],
                HeapAfter = [80, 180, 280],
                DurationMicros = [10, 20, 30],
                Ticks = [1, 2, 3],
                AllocationRateBytesPerMinute = [1000, 2000, 3000],
            }));

            SendBatch(port, BatchType.Allocations, WireCodec.Serialize(new AllocationsBatch {
                WindowStartTimestamps = [10, 20],
                WindowDurationsMs = [5, 5],
                BytesAllocated = [4096, 8192],
                SamplesCount = [1, 1],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/status");
                _out.WriteLine($"status: {body}");
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement recv = doc.RootElement.GetProperty("receive");
                long gc = recv.GetProperty("total_gc_events").GetInt64();
                long alloc = recv.GetProperty("total_allocations").GetInt64();
                return gc >= 3 && alloc >= 2;
            }, TimeSpan.FromSeconds(3));

            string statusBody = await http.GetStringAsync("/api/v1/status");
            using JsonDocument status = JsonDocument.Parse(statusBody);
            JsonElement receive = status.RootElement.GetProperty("receive");
            receive.GetProperty("total_gc_events").GetInt64().Should().Be(3);
            receive.GetProperty("total_allocations").GetInt64().Should().Be(2);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Gc_endpoint_returns_recent_events_newest_first_and_honors_limit() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.GcEvents, WireCodec.Serialize(new GcEventsBatch {
                Generations = [0, 1, 2],
                PauseTypes = [0, 0, 1],
                HeapBefore = [100, 200, 300],
                HeapAfter = [80, 180, 280],
                DurationMicros = [10, 20, 30],
                Ticks = [111, 222, 333],
                AllocationRateBytesPerMinute = [1000, 2000, 3000],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/sessions/current/gc");
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("events").GetArrayLength() >= 3;
            }, TimeSpan.FromSeconds(3));

            string fullBody = await http.GetStringAsync("/api/v1/sessions/current/gc");
            _out.WriteLine($"gc full: {fullBody}");
            using JsonDocument full = JsonDocument.Parse(fullBody);
            full.RootElement.GetProperty("total_events").GetInt64().Should().Be(3);
            JsonElement events = full.RootElement.GetProperty("events");
            events.GetArrayLength().Should().Be(3);
            events[0].GetProperty("ticks").GetInt64().Should().Be(333);
            events[0].GetProperty("generation").GetInt32().Should().Be(2);
            events[1].GetProperty("ticks").GetInt64().Should().Be(222);
            events[2].GetProperty("ticks").GetInt64().Should().Be(111);

            string limited = await http.GetStringAsync("/api/v1/sessions/current/gc?limit=2");
            _out.WriteLine($"gc limited: {limited}");
            using JsonDocument lim = JsonDocument.Parse(limited);
            JsonElement limEvents = lim.RootElement.GetProperty("events");
            limEvents.GetArrayLength().Should().Be(2);
            limEvents[0].GetProperty("ticks").GetInt64().Should().Be(333);
            limEvents[1].GetProperty("ticks").GetInt64().Should().Be(222);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Post_without_origin_header_is_rejected_with_403() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            HttpResponseMessage noOrigin = await http.PostAsync("/api/v1/anything", new StringContent(""));
            noOrigin.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

            using HttpRequestMessage wrongOrigin = new(HttpMethod.Post, "/api/v1/anything") {
                Content = new StringContent(""),
            };
            wrongOrigin.Headers.Add("Origin", "http://evil.example.com");
            HttpResponseMessage wrongResp = await http.SendAsync(wrongOrigin);
            wrongResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

            using HttpRequestMessage goodOrigin = new(HttpMethod.Post, "/api/v1/anything") {
                Content = new StringContent(""),
            };
            goodOrigin.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            HttpResponseMessage goodResp = await http.SendAsync(goodOrigin);
            goodResp.StatusCode.Should().NotBe(System.Net.HttpStatusCode.Forbidden);

            HttpResponseMessage getNoOrigin = await http.GetAsync("/api/v1/status");
            getNoOrigin.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Post_with_valid_origin_but_missing_bearer_is_rejected_with_401() {
        int port = PickFreePort();
        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("test-bearer-token-abc");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            using HttpRequestMessage noBearer = new(HttpMethod.Post, "/api/v1/anything") {
                Content = new StringContent(""),
            };
            noBearer.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            HttpResponseMessage noBearerResp = await http.SendAsync(noBearer);
            noBearerResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
            noBearerResp.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");

            using HttpRequestMessage wrongBearer = new(HttpMethod.Post, "/api/v1/anything") {
                Content = new StringContent(""),
            };
            wrongBearer.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            wrongBearer.Headers.Add("Authorization", "Bearer wrong-token");
            HttpResponseMessage wrongResp = await http.SendAsync(wrongBearer);
            wrongResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

            using HttpRequestMessage goodBearer = new(HttpMethod.Post, "/api/v1/anything") {
                Content = new StringContent(""),
            };
            goodBearer.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            goodBearer.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage goodResp = await http.SendAsync(goodBearer);
            goodResp.StatusCode.Should().NotBe(System.Net.HttpStatusCode.Unauthorized);
            goodResp.StatusCode.Should().NotBe(System.Net.HttpStatusCode.Forbidden);

            HttpResponseMessage getNoAuth = await http.GetAsync("/api/v1/status");
            getNoAuth.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hotspots_endpoint_returns_sections_sorted_by_total_descending_and_honors_limit() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "hot-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.SectionRegistrations, WireCodec.Serialize(new SectionRegistrationsBatch {
                SectionIds = [10, 20, 30],
                Names = ["hot.cold", "hot.warm", "hot.peak"],
            }));

            SendBatch(port, BatchType.Sections, WireCodec.Serialize(new SectionBatch {
                SectionIds = [10, 20, 20, 30, 30, 30],
                StartTimestamps = [1, 2, 3, 4, 5, 6],
                ElapsedTicks = [100, 500, 500, 1000, 1000, 1000],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/status");
                using JsonDocument doc = JsonDocument.Parse(body);
                int sectionCount = doc.RootElement.GetProperty("receive").GetProperty("section_count").GetInt32();
                return sectionCount >= 3;
            }, TimeSpan.FromSeconds(3));

            string hotspotsBody = await http.GetStringAsync("/api/v1/sessions/current/hotspots?limit=2");
            _out.WriteLine($"hotspots: {hotspotsBody}");
            using JsonDocument doc = JsonDocument.Parse(hotspotsBody);
            JsonElement hotspots = doc.RootElement.GetProperty("hotspots");

            hotspots.GetArrayLength().Should().Be(2);
            hotspots[0].GetProperty("name").GetString().Should().Be("hot.peak");
            hotspots[1].GetProperty("name").GetString().Should().Be("hot.warm");
            hotspots[0].GetProperty("total_ns").GetInt64().Should().BeGreaterThan(hotspots[1].GetProperty("total_ns").GetInt64());
            hotspots[0].GetProperty("mean_ns").GetInt64().Should().BeGreaterThan(0);
            hotspots[0].GetProperty("sample_count").GetInt32().Should().Be(3);
            hotspots[1].GetProperty("sample_count").GetInt32().Should().Be(2);
            hotspots[0].GetProperty("p50_ns").GetInt64().Should().BeGreaterThan(0);
            hotspots[0].GetProperty("p95_ns").GetInt64()
                .Should().BeGreaterOrEqualTo(hotspots[0].GetProperty("p50_ns").GetInt64());
            hotspots[0].GetProperty("p99_ns").GetInt64()
                .Should().BeGreaterOrEqualTo(hotspots[0].GetProperty("p95_ns").GetInt64());
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Section_timeseries_endpoint_returns_buckets_for_known_section_and_404_for_unknown() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "ts-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.SectionRegistrations, WireCodec.Serialize(new SectionRegistrationsBatch {
                SectionIds = [77],
                Names = ["ts.section"],
            }));

            SendBatch(port, BatchType.Sections, WireCodec.Serialize(new SectionBatch {
                SectionIds = [77, 77, 77, 77],
                StartTimestamps = [1, 2, 3, 4],
                ElapsedTicks = [100, 200, 300, 400],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/status");
                using JsonDocument doc = JsonDocument.Parse(body);
                int sectionCount = doc.RootElement.GetProperty("receive").GetProperty("section_count").GetInt32();
                return sectionCount >= 1;
            }, TimeSpan.FromSeconds(3));

            string body = await http.GetStringAsync("/api/v1/sessions/current/sections/77/timeseries");
            _out.WriteLine($"timeseries: {body}");
            using JsonDocument doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("id").GetInt32().Should().Be(77);
            doc.RootElement.GetProperty("name").GetString().Should().Be("ts.section");
            doc.RootElement.GetProperty("bucket_seconds").GetInt32().Should().Be(1);
            JsonElement points = doc.RootElement.GetProperty("points");
            points.GetArrayLength().Should().BeGreaterOrEqualTo(1);
            long totalCount = 0;
            long totalNs = 0;
            foreach (JsonElement p in points.EnumerateArray()) {
                totalCount += p.GetProperty("count").GetInt64();
                totalNs += p.GetProperty("total_ns").GetInt64();
                p.GetProperty("mean_ns").GetInt64().Should().BeGreaterThan(0);
            }
            totalCount.Should().Be(4);
            totalNs.Should().BeGreaterThan(0);

            HttpResponseMessage missing = await http.GetAsync("/api/v1/sessions/current/sections/9999/timeseries");
            missing.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Call_tree_endpoint_returns_nested_roots_from_parent_ids() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "tree-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.SectionRegistrations, WireCodec.Serialize(new SectionRegistrationsBatch {
                SectionIds = [10, 20],
                Names = ["tree.root", "tree.child"],
            }));

            SendBatch(port, BatchType.Sections, WireCodec.Serialize(new SectionBatch {
                SectionIds = [10, 20, 20],
                ParentIds = [-1, 10, 10],
                StartTimestamps = [1, 2, 3],
                ElapsedTicks = [1000, 200, 300],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/status");
                using JsonDocument doc = JsonDocument.Parse(body);
                int sectionCount = doc.RootElement.GetProperty("receive").GetProperty("section_count").GetInt32();
                return sectionCount >= 2;
            }, TimeSpan.FromSeconds(3));

            string treeBody = await http.GetStringAsync("/api/v1/sessions/current/call_tree");
            _out.WriteLine($"call_tree: {treeBody}");
            using JsonDocument doc = JsonDocument.Parse(treeBody);
            JsonElement roots = doc.RootElement.GetProperty("roots");

            roots.GetArrayLength().Should().Be(1);
            JsonElement root = roots[0];
            root.GetProperty("id").GetInt32().Should().Be(10);
            root.GetProperty("name").GetString().Should().Be("tree.root");
            root.GetProperty("call_count").GetInt64().Should().Be(1);

            JsonElement children = root.GetProperty("children");
            children.GetArrayLength().Should().Be(1);
            JsonElement child = children[0];
            child.GetProperty("id").GetInt32().Should().Be(20);
            child.GetProperty("name").GetString().Should().Be("tree.child");
            child.GetProperty("call_count").GetInt64().Should().Be(2);
            child.GetProperty("total_ns").GetInt64().Should().BeGreaterThan(0);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    [Fact]
    public async Task Metrics_endpoint_returns_registered_metrics_with_labels() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "metrics-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.MetricRegistrations, WireCodec.Serialize(new MetricRegistrationsBatch {
                MetricIds = [101, 102],
                Names = ["my.mod.frames_drawn", "my.mod.heap_used"],
                Kinds = [0, 1],
                Units = ["count", "bytes"],
            }));

            SendBatch(port, BatchType.Metrics, WireCodec.Serialize(new MetricsBatch {
                MetricIds = [101, 101, 102],
                LabelCanonicals = ["scene=map", "scene=ui", ""],
                Kinds = [0, 0, 1],
                Values = [42, 17, 1048576],
                SampleCounts = [3, 1, 1],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/sessions/current/metrics");
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("metrics").GetArrayLength() >= 2;
            }, TimeSpan.FromSeconds(3));

            string metricsBody = await http.GetStringAsync("/api/v1/sessions/current/metrics");
            _out.WriteLine($"metrics: {metricsBody}");
            using JsonDocument doc = JsonDocument.Parse(metricsBody);
            JsonElement metrics = doc.RootElement.GetProperty("metrics");
            metrics.GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("total_observations").GetInt64().Should().Be(3);

            JsonElement frames = Enumerable.Range(0, metrics.GetArrayLength())
                .Select(i => metrics[i])
                .Single(m => m.GetProperty("id").GetInt32() == 101);
            frames.GetProperty("name").GetString().Should().Be("my.mod.frames_drawn");
            frames.GetProperty("unit").GetString().Should().Be("count");
            JsonElement framesLabels = frames.GetProperty("labels");
            framesLabels.GetArrayLength().Should().Be(2);
            JsonElement mapLabel = Enumerable.Range(0, framesLabels.GetArrayLength())
                .Select(i => framesLabels[i])
                .Single(l => l.GetProperty("canonical").GetString() == "scene=map");
            mapLabel.GetProperty("latest_value").GetInt64().Should().Be(42);
            mapLabel.GetProperty("total_sample_count").GetInt64().Should().Be(3);

            JsonElement heap = Enumerable.Range(0, metrics.GetArrayLength())
                .Select(i => metrics[i])
                .Single(m => m.GetProperty("id").GetInt32() == 102);
            heap.GetProperty("unit").GetString().Should().Be("bytes");
            JsonElement heapLabels = heap.GetProperty("labels");
            heapLabels.GetArrayLength().Should().Be(1);
            heapLabels[0].GetProperty("latest_value").GetInt64().Should().Be(1048576);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    [Fact]
    public async Task Logs_endpoint_returns_ring_buffer_entries_newest_first() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            Logging.RingBufferLogSink sink = app.Services.GetRequiredService<Logging.RingBufferLogSink>();
            Serilog.ILogger logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
                .WriteTo.Sink(sink)
                .CreateLogger();

            logger.Information("hello {Who}", "world");
            logger.Warning("careful {N}", 42);
            logger.Error("boom {Code}", "E1");

            string body = await http.GetStringAsync("/api/v1/logs?limit=10");
            _out.WriteLine($"logs: {body}");
            using JsonDocument doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("schema_version").GetInt32().Should().Be(SchemaVersion.Current);
            doc.RootElement.GetProperty("count").GetInt32().Should().Be(3);
            JsonElement entries = doc.RootElement.GetProperty("entries");
            entries[0].GetProperty("message").GetString().Should().Contain("boom");
            entries[0].GetProperty("level").GetString().Should().Be("Error");
            entries[1].GetProperty("message").GetString().Should().Contain("careful");
            entries[2].GetProperty("message").GetString().Should().Contain("hello");

            string warnPlus = await http.GetStringAsync("/api/v1/logs?level=Warning&limit=10");
            _out.WriteLine($"logs warn+: {warnPlus}");
            using JsonDocument warnDoc = JsonDocument.Parse(warnPlus);
            warnDoc.RootElement.GetProperty("count").GetInt32().Should().Be(2);
            foreach (JsonElement e in warnDoc.RootElement.GetProperty("entries").EnumerateArray())
                e.GetProperty("level").GetString().Should().NotBe("Information");

            string capped = await http.GetStringAsync("/api/v1/logs?limit=2");
            using JsonDocument capDoc = JsonDocument.Parse(capped);
            capDoc.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logs_endpoint_rejects_unknown_level_with_400() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            HttpResponseMessage bad = await http.GetAsync("/api/v1/logs?level=Bogus");
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using JsonDocument doc = JsonDocument.Parse(await bad.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("schema_version").GetInt32().Should().Be(SchemaVersion.Current);
            doc.RootElement.GetProperty("reason").GetString().Should().Contain("Bogus");
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Root_serves_embedded_dashboard_spa_html() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            HttpResponseMessage rootResponse = await http.GetAsync("/");
            rootResponse.IsSuccessStatusCode.Should().BeTrue();
            rootResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
            string html = await rootResponse.Content.ReadAsStringAsync();
            html.Should().Contain("<!doctype html>");
            html.Should().Contain("/assets/");

            HttpResponseMessage unknownRoute = await http.GetAsync("/overview");
            unknownRoute.IsSuccessStatusCode.Should().BeTrue();
            (await unknownRoute.Content.ReadAsStringAsync()).Should().Contain("<!doctype html>");

            HttpResponseMessage unknownApi = await http.GetAsync("/api/v1/does-not-exist");
            unknownApi.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }


    [Fact]
    public async Task Ping_datagram_receives_pong_with_collector_version_and_session() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "ping-session",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/status");
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("session").GetProperty("id").GetString() == "ping-session";
            }, TimeSpan.FromSeconds(3));

            PingMessage ping = new() { OwnerId = "smoke.owner", SentAtUtcTicks = 999 };
            TelemetryBatch envelope = new() {
                SchemaVersion = SchemaVersion.Current,
                Sequence = 1,
                OwnerId = "smoke",
                BatchType = BatchType.Ping,
                Payload = WireCodec.Serialize(ping),
            };
            byte[] datagram = WireCodec.Serialize(envelope);

            using UdpClient client = new(AddressFamily.InterNetwork);
            client.Client.ReceiveTimeout = 2000;
            client.Connect("127.0.0.1", port);
            client.Send(datagram, datagram.Length);

            IPEndPoint remote = new(IPAddress.Any, 0);
            byte[] response = client.Receive(ref remote);
            TelemetryBatch pongEnvelope = WireCodec.Deserialize<TelemetryBatch>(response);
            pongEnvelope.BatchType.Should().Be(BatchType.Pong);
            PongMessage pong = WireCodec.Deserialize<PongMessage>(pongEnvelope.Payload);
            pong.OwnerId.Should().Be("smoke.owner");
            pong.PingSentAtUtcTicks.Should().Be(999);
            pong.CollectorVersion.Should().Be(BuildInfo.Revision);
            pong.SessionId.Should().Be("ping-session");
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Config_get_returns_full_default_document_with_snake_case_keys() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            string body = await http.GetStringAsync("/api/v1/config");
            _out.WriteLine($"config: {body}");
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            root.GetProperty("schema_version").GetInt32().Should().Be(1);
            root.GetProperty("collector").GetProperty("listen_address").GetString().Should().Be("127.0.0.1");
            root.GetProperty("collector").GetProperty("port").GetInt32().Should().Be(17654);
            root.GetProperty("sections").GetProperty("disabled").GetArrayLength().Should().Be(0);
            root.GetProperty("session").GetProperty("slow_tick_threshold_us").GetInt32().Should().Be(16667);
            root.GetProperty("storage").GetProperty("sqlite_journal_mode").GetString().Should().Be("WAL");
            root.GetProperty("privacy").GetProperty("include_assembly_versions_and_patches").GetBoolean().Should().BeTrue();
            root.GetProperty("i18n").GetProperty("default_language").GetString().Should().Be("en");
            root.GetProperty("exporters").GetProperty("prometheus_port").GetInt32().Should().Be(7879);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Config_post_replaces_document_and_get_reflects_it() {
        int port = PickFreePort();
        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("config-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/config") {
                Content = new StringContent(
                    "{\"schema_version\":1,\"collector\":{\"log_level\":\"Debug\",\"port\":18000},\"sections\":{\"disabled\":[\"core.tick\",\"core.path\"]}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage postResp = await http.SendAsync(post);
            postResp.StatusCode.Should().Be(HttpStatusCode.OK);

            string after = await http.GetStringAsync("/api/v1/config");
            _out.WriteLine($"config after: {after}");
            using JsonDocument doc = JsonDocument.Parse(after);
            JsonElement collector = doc.RootElement.GetProperty("collector");
            collector.GetProperty("log_level").GetString().Should().Be("Debug");
            collector.GetProperty("port").GetInt32().Should().Be(18000);
            collector.GetProperty("listen_address").GetString().Should().Be("127.0.0.1");
            JsonElement disabled = doc.RootElement.GetProperty("sections").GetProperty("disabled");
            disabled.GetArrayLength().Should().Be(2);
            disabled[0].GetString().Should().Be("core.tick");
            disabled[1].GetString().Should().Be("core.path");
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Config_post_with_unsupported_schema_version_returns_400() {
        int port = PickFreePort();
        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("config-bearer-token-2");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/config") {
                Content = new StringContent(
                    "{\"schema_version\":999}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage postResp = await http.SendAsync(post);
            postResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Panels_register_and_list_round_trips_per_owner() {
        int port = PickFreePort();
        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("panels-bearer-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            string empty = await http.GetStringAsync("/api/v1/panels");
            using (JsonDocument emptyDoc = JsonDocument.Parse(empty))
                emptyDoc.RootElement.GetProperty("owners").GetArrayLength().Should().Be(0);

            await RegisterPanels(http, token, port,
                "{\"schema_version\":1,\"owner_id\":\"mod.a\",\"panels\":[{\"id\":\"combat\",\"title\":\"Combat\",\"icon\":\"sword\",\"layout\":[{\"metric\":\"mod_a_hits\",\"widget\":\"counter\"}]}]}");
            await RegisterPanels(http, token, port,
                "{\"schema_version\":1,\"owner_id\":\"mod.b\",\"panels\":[{\"id\":\"econ\",\"title\":\"Economy\",\"icon\":\"coin\",\"layout\":[{\"metric\":\"mod_b_silver\",\"widget\":\"gauge\"}]}]}");

            string both = await http.GetStringAsync("/api/v1/panels");
            _out.WriteLine($"panels: {both}");
            using (JsonDocument bothDoc = JsonDocument.Parse(both)) {
                JsonElement owners = bothDoc.RootElement.GetProperty("owners");
                owners.GetArrayLength().Should().Be(2);
                JsonElement a = FindOwner(owners, "mod.a");
                a.GetProperty("panels")[0].GetProperty("id").GetString().Should().Be("combat");
                a.GetProperty("panels")[0].GetProperty("layout")[0].GetProperty("widget").GetString().Should().Be("counter");
            }

            await RegisterPanels(http, token, port,
                "{\"schema_version\":1,\"owner_id\":\"mod.a\",\"panels\":[{\"id\":\"medical\",\"title\":\"Medical\",\"icon\":\"cross\",\"layout\":[{\"metric\":\"mod_a_surgeries\",\"widget\":\"top_n\"}]}]}");

            string replaced = await http.GetStringAsync("/api/v1/panels");
            using (JsonDocument replacedDoc = JsonDocument.Parse(replaced)) {
                JsonElement owners = replacedDoc.RootElement.GetProperty("owners");
                owners.GetArrayLength().Should().Be(2);
                JsonElement a = FindOwner(owners, "mod.a");
                a.GetProperty("panels").GetArrayLength().Should().Be(1);
                a.GetProperty("panels")[0].GetProperty("id").GetString().Should().Be("medical");
                FindOwner(owners, "mod.b").GetProperty("panels")[0].GetProperty("id").GetString().Should().Be("econ");
            }
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Panels_register_rejects_unknown_widget() {
        int port = PickFreePort();
        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("panels-bearer-token-2");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/panels/register") {
                Content = new StringContent(
                    "{\"schema_version\":1,\"owner_id\":\"mod.a\",\"panels\":[{\"id\":\"x\",\"title\":\"X\",\"icon\":\"i\",\"layout\":[{\"metric\":\"m\",\"widget\":\"frobnicator\"}]}]}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage resp = await http.SendAsync(post);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using (JsonDocument list = JsonDocument.Parse(await http.GetStringAsync("/api/v1/panels")))
                list.RootElement.GetProperty("owners").GetArrayLength().Should().Be(0);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Panels_refresh_flag_sets_and_reports_ttl() {
        int port = PickFreePort();
        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("panels-bearer-token-3");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            string before = await http.GetStringAsync("/api/v1/panels/refresh_requested");
            using (JsonDocument beforeDoc = JsonDocument.Parse(before))
                beforeDoc.RootElement.GetProperty("refresh_requested").GetBoolean().Should().BeFalse();

            using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/panels/refresh_requested") {
                Content = new StringContent("", System.Text.Encoding.UTF8, "application/json"),
            };
            post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
            post.Headers.Add("Authorization", $"Bearer {token.Value}");
            HttpResponseMessage postResp = await http.SendAsync(post);
            postResp.StatusCode.Should().Be(HttpStatusCode.OK);

            string after = await http.GetStringAsync("/api/v1/panels/refresh_requested");
            _out.WriteLine($"refresh: {after}");
            using JsonDocument afterDoc = JsonDocument.Parse(after);
            afterDoc.RootElement.GetProperty("refresh_requested").GetBoolean().Should().BeTrue();
            int remaining = afterDoc.RootElement.GetProperty("ttl_seconds_remaining").GetInt32();
            remaining.Should().BeGreaterThan(0);
            remaining.Should().BeLessThanOrEqualTo(30);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task RegisterPanels(HttpClient http, Security.CollectorToken token, int port, string json) {
        using HttpRequestMessage post = new(HttpMethod.Post, "/api/v1/panels/register") {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        post.Headers.Add("Origin", $"http://127.0.0.1:{port}");
        post.Headers.Add("Authorization", $"Bearer {token.Value}");
        HttpResponseMessage resp = await http.SendAsync(post);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static JsonElement FindOwner(JsonElement owners, string ownerId) {
        foreach (JsonElement owner in owners.EnumerateArray()) {
            if (owner.GetProperty("owner_id").GetString() == ownerId)
                return owner;
        }

        throw new Xunit.Sdk.XunitException($"owner '{ownerId}' not found");
    }

    [Fact]
    public async Task Sessions_endpoints_report_current_session_and_summary() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            HttpResponseMessage beforeCurrent = await http.GetAsync("/api/v1/sessions/current");
            beforeCurrent.StatusCode.Should().Be(HttpStatusCode.NotFound);
            HttpResponseMessage beforeSummary = await http.GetAsync("/api/v1/sessions/current/summary");
            beforeSummary.StatusCode.Should().Be(HttpStatusCode.NotFound);

            string emptyList = await http.GetStringAsync("/api/v1/sessions");
            using (JsonDocument emptyDoc = JsonDocument.Parse(emptyList))
                emptyDoc.RootElement.GetProperty("sessions").GetArrayLength().Should().Be(0);

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "sessions-smoke",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.SectionRegistrations, WireCodec.Serialize(new SectionRegistrationsBatch {
                SectionIds = [42, 43],
                Names = ["smoke.tick", "smoke.path"],
            }));

            SendBatch(port, BatchType.Sections, WireCodec.Serialize(new SectionBatch {
                SectionIds = [42, 42, 43],
                StartTimestamps = [1000, 2000, 3000],
                ElapsedTicks = [500, 600, 700],
            }));

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/sessions/current");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            string listBody = await http.GetStringAsync("/api/v1/sessions");
            _out.WriteLine($"sessions: {listBody}");
            using (JsonDocument listDoc = JsonDocument.Parse(listBody)) {
                JsonElement sessions = listDoc.RootElement.GetProperty("sessions");
                sessions.GetArrayLength().Should().Be(1);
                JsonElement only = sessions[0];
                only.GetProperty("id").GetString().Should().Be("sessions-smoke");
                only.GetProperty("is_current").GetBoolean().Should().BeTrue();
                only.GetProperty("library_version").GetString().Should().Be("0.0.0-smoke");
                only.GetProperty("game_version").GetString().Should().Be("1.6");
            }

            string currentBody = await http.GetStringAsync("/api/v1/sessions/current");
            using (JsonDocument currentDoc = JsonDocument.Parse(currentBody)) {
                JsonElement root = currentDoc.RootElement;
                root.GetProperty("session").GetProperty("id").GetString().Should().Be("sessions-smoke");
                root.GetProperty("session").GetProperty("is_current").GetBoolean().Should().BeTrue();
                root.GetProperty("receive").GetProperty("total_samples").GetInt32().Should().BeGreaterThanOrEqualTo(3);
                root.GetProperty("receive").GetProperty("section_count").GetInt32().Should().Be(2);
            }

            string summaryBody = await http.GetStringAsync("/api/v1/sessions/current/summary");
            _out.WriteLine($"summary: {summaryBody}");
            using (JsonDocument summaryDoc = JsonDocument.Parse(summaryBody)) {
                JsonElement root = summaryDoc.RootElement;
                root.GetProperty("session").GetProperty("id").GetString().Should().Be("sessions-smoke");
                root.GetProperty("section_count").GetInt32().Should().Be(2);
                root.GetProperty("total_samples").GetInt32().Should().BeGreaterThanOrEqualTo(3);
                root.GetProperty("total_section_ns").GetInt64().Should().BeGreaterThan(0);
            }
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sessions_list_includes_persisted_sessions_from_disk() {
        int port = PickFreePort();
        string sessionsDir = Path.Combine(Path.GetTempPath(), "rimobs-sessions-" + Guid.NewGuid().ToString("N"));
        using (Storage.SqliteSessionPersister seed = new(sessionsDir)) {
            seed.WriteSessionMeta(new SessionMeta {
                SessionId = "persisted-old",
                StartedUtcTicks = DateTime.UtcNow.AddHours(-1).Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-old",
                GameVersion = "1.6",
            });
        }

        WebApplication app = Program.BuildApp([], port, Security.CollectorToken.CreateFromEnvOrGenerate(), sessionsDir);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            string listBody = await http.GetStringAsync("/api/v1/sessions");
            _out.WriteLine($"persisted sessions: {listBody}");
            using JsonDocument listDoc = JsonDocument.Parse(listBody);
            JsonElement sessions = listDoc.RootElement.GetProperty("sessions");
            bool sawPersisted = false;
            foreach (JsonElement s in sessions.EnumerateArray()) {
                if (s.GetProperty("id").GetString() == "persisted-old") {
                    sawPersisted = true;
                    s.GetProperty("is_current").GetBoolean().Should().BeFalse();
                }
            }
            sawPersisted.Should().BeTrue();
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(sessionsDir, recursive: true); } catch { }
        }
    }

    private static void SendBatch(int port, BatchType type, byte[] payload) {
        TelemetryBatch envelope = new() {
            SchemaVersion = SchemaVersion.Current,
            Sequence = 1,
            OwnerId = "smoke",
            BatchType = type,
            Payload = payload,
        };
        byte[] bytes = WireCodec.Serialize(envelope);
        using UdpClient client = new();
        client.Send(bytes, bytes.Length, "127.0.0.1", port);
    }

    private static async Task WaitFor(Func<Task<bool>> condition, TimeSpan timeout) {
        DateTime deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline) {
            try {
                if (await condition())
                    return;
            }
            catch (Exception ex) {
                lastError = ex;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("WaitFor condition never became true", lastError);
    }

    [Fact]
    public async Task Instrumentation_endpoints_proxy_to_control_server_after_handshake() {
        int collectorPort = PickFreePort();
        using StubControlServer stub = new("topsecret");
        stub.OnSearch = req => new ControlSearchResponse {
            Results = [new ControlMethodDescriptor {
                TypeFullName = "A", MethodName = "B", Signature = "A:B()",
                ParamTypeFullNames = [], AssemblyName = "A",
            }],
        };
        stub.OnPatch = _ => new ControlPatchResponse {
            PatchId = 7,
            SectionId = 3,
            SectionName = "test.dynamic.A:B",
            Status = PatchStatus.Active,
        };
        stub.Start();

        Security.CollectorToken token = Security.CollectorToken.FromExplicitValue("instrumentation-bearer-token");
        WebApplication app = Program.BuildApp([], collectorPort, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{collectorPort}") };
            http.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{collectorPort}");
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.Value}");

            await WaitFor(async () => (await http.GetAsync("/api/v1/status")).IsSuccessStatusCode, TimeSpan.FromSeconds(3));

            SendBatch(collectorPort, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "live",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0",
                GameVersion = "1.6",
                ControlPort = stub.Port,
                ControlSecret = "topsecret",
            }));

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/instrumentation/search?q=B");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            HttpResponseMessage searchRes = await http.GetAsync("/api/v1/instrumentation/search?q=B");
            searchRes.StatusCode.Should().Be(HttpStatusCode.OK);

            HttpResponseMessage patchRes = await http.PostAsJsonAsync(
                "/api/v1/instrumentation/patch",
                new { typeFullName = "A", methodName = "B", paramTypeFullNames = Array.Empty<string>() });
            patchRes.StatusCode.Should().Be(HttpStatusCode.OK);

            HttpResponseMessage listRes = await http.GetAsync("/api/v1/instrumentation/patches");
            listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sections_endpoint_returns_registered_sections_with_subsystem() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            await WaitFor(async () => {
                HttpResponseMessage r = await http.GetAsync("/api/v1/status");
                return r.IsSuccessStatusCode;
            }, TimeSpan.FromSeconds(3));

            SendBatch(port, BatchType.SessionMeta, WireCodec.Serialize(new SessionMeta {
                SessionId = "sections-endpoint-smoke",
                StartedUtcTicks = DateTime.UtcNow.Ticks,
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                LibraryVersion = "0.0.0-smoke",
                GameVersion = "1.6",
            }));

            SendBatch(port, BatchType.SectionRegistrations, WireCodec.Serialize(new SectionRegistrationsBatch {
                SectionIds = [1, 2],
                Names = ["pawns.work", "core.tick"],
                Subsystems = ["pawns", null],
            }));

            await WaitFor(async () => {
                string body = await http.GetStringAsync("/api/v1/sections");
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("sections").GetArrayLength() >= 2;
            }, TimeSpan.FromSeconds(3));

            string sectionsBody = await http.GetStringAsync("/api/v1/sections");
            _out.WriteLine($"sections: {sectionsBody}");
            using JsonDocument sectionsDoc = JsonDocument.Parse(sectionsBody);
            JsonElement sections = sectionsDoc.RootElement.GetProperty("sections");
            sections.GetArrayLength().Should().Be(2);

            JsonElement? withSub = null;
            JsonElement? noSub = null;
            foreach (JsonElement s in sections.EnumerateArray()) {
                if (s.GetProperty("id").GetInt32() == 1) withSub = s;
                if (s.GetProperty("id").GetInt32() == 2) noSub = s;
            }

            withSub.Should().NotBeNull();
            withSub!.Value.GetProperty("name").GetString().Should().Be("pawns.work");
            withSub.Value.GetProperty("subsystem").GetString().Should().Be("pawns");

            noSub.Should().NotBeNull();
            noSub!.Value.GetProperty("name").GetString().Should().Be("core.tick");
            noSub.Value.GetProperty("subsystem").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
