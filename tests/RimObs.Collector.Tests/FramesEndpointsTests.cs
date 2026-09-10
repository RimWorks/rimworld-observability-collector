using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Security;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class FramesEndpointsTests {
    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Latest_returns_the_newest_sealed_frame_in_microseconds() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-test-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "frames-endpoint",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20, 10],
            ParentIds = [-1, 10, -1],
            StartTimestamps = [100L, 150L, 700L],
            ElapsedTicks = [500L, 200L, 400L],
            FrameOrdinals = [1, 1, 2],
        });
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement frame = doc.RootElement.GetProperty("frame");

            frame.GetProperty("capture_ordinal").GetInt32().Should().Be(1);
            frame.GetProperty("node_count").GetInt32().Should().Be(2);
            frame.GetProperty("duration_us").GetDouble().Should().BeApproximately(50.0, 0.01);
            frame.GetProperty("nodes").GetProperty("section_ids").GetArrayLength().Should().Be(2);
            doc.RootElement.GetProperty("stats").GetProperty("frame_count").GetInt32().Should().Be(1);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_subtracts_the_session_anchor_from_starts_but_not_durations() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-anchor-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "frames-anchor",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 1000L,
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20, 10],
            ParentIds = [-1, 10, -1],
            StartTimestamps = [1100L, 1150L, 1700L],
            ElapsedTicks = [500L, 200L, 400L],
            FrameOrdinals = [1, 1, 2],
        });
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement frame = doc.RootElement.GetProperty("frame");
            JsonElement nodes = frame.GetProperty("nodes");

            frame.GetProperty("start_us").GetDouble().Should().BeApproximately(10.0, 0.01);
            frame.GetProperty("end_us").GetDouble().Should().BeApproximately(60.0, 0.01);
            frame.GetProperty("duration_us").GetDouble().Should().BeApproximately(50.0, 0.01);

            nodes.GetProperty("parent_ids")[1].GetInt32().Should().Be(10);
            nodes.GetProperty("start_us")[0].GetDouble().Should().BeApproximately(10.0, 0.01);
            nodes.GetProperty("start_us")[1].GetDouble().Should().BeApproximately(15.0, 0.01);
            nodes.GetProperty("dur_us")[0].GetDouble().Should().BeApproximately(50.0, 0.01);
            nodes.GetProperty("dur_us")[1].GetDouble().Should().BeApproximately(20.0, 0.01);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_includes_node_ids_and_parent_node_ids_as_plain_ints() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-node-ids-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "frames-node-ids",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20, 10],
            ParentIds = [-1, 10, -1],
            NodeIds = [500, 501, 502],
            ParentNodeIds = [-1, 500, -1],
            StartTimestamps = [100L, 150L, 700L],
            ElapsedTicks = [500L, 200L, 400L],
            FrameOrdinals = [1, 1, 2],
        });
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement nodes = doc.RootElement.GetProperty("frame").GetProperty("nodes");
            JsonElement sectionIds = nodes.GetProperty("section_ids");
            JsonElement nodeIds = nodes.GetProperty("node_ids");
            JsonElement parentNodeIds = nodes.GetProperty("parent_node_ids");

            nodeIds.GetArrayLength().Should().Be(sectionIds.GetArrayLength());
            parentNodeIds.GetArrayLength().Should().Be(sectionIds.GetArrayLength());
            nodeIds[0].GetInt32().Should().Be(500);
            nodeIds[1].GetInt32().Should().Be(501);
            parentNodeIds[0].GetInt32().Should().Be(-1);
            parentNodeIds[1].GetInt32().Should().Be(500);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_returns_a_null_frame_before_any_frame_seals() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-empty-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);

            doc.RootElement.GetProperty("frame").ValueKind.Should().Be(JsonValueKind.Null);
            doc.RootElement.GetProperty("stats").GetProperty("frame_count").GetInt32().Should().Be(0);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_reports_the_session_stopwatch_frequency() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-freq-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "frames-freq",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);

            doc.RootElement.GetProperty("stopwatch_frequency").GetInt64().Should().Be(10_000_000L);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_reports_zero_stopwatch_frequency_before_any_session_meta() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-freq-empty-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);

            doc.RootElement.GetProperty("stopwatch_frequency").GetInt64().Should().Be(0L);
        }
        finally {
            await app.StopAsync();
        }
    }

    private static void SeedFiveFrames(WebApplication app, string sessionId) {
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = sessionId,
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 10, 10, 10, 10, 10],
            ParentIds = [-1, -1, -1, -1, -1, -1],
            StartTimestamps = [100L, 1100L, 2100L, 3100L, 4100L, 5100L],
            ElapsedTicks = [500L, 500L, 500L, 500L, 500L, 500L],
            FrameOrdinals = [1, 2, 3, 4, 5, 6],
        });
    }

    private static int[] Ordinals(JsonDocument doc) {
        return [.. doc.RootElement.GetProperty("frames").EnumerateArray()
            .Select(f => f.GetProperty("capture_ordinal").GetInt32())];
    }

    [Fact]
    public async Task Range_returns_a_run_of_frames_from_the_asked_ordinal() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-range-token");
        WebApplication app = Program.BuildApp([], port, token);
        SeedFiveFrames(app, "frames-range");
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames?from=2&count=3");
            using JsonDocument doc = JsonDocument.Parse(body);

            Ordinals(doc).Should().Equal(2, 3, 4);
            doc.RootElement.GetProperty("frames")[0].GetProperty("nodes")
                .GetProperty("section_ids").GetArrayLength().Should().Be(1);
            doc.RootElement.GetProperty("stats").GetProperty("frame_count").GetInt32().Should().Be(5);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Range_without_a_from_returns_the_newest_frames() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-range-newest-token");
        WebApplication app = Program.BuildApp([], port, token);
        SeedFiveFrames(app, "frames-range-newest");
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames?count=2");
            using JsonDocument doc = JsonDocument.Parse(body);

            Ordinals(doc).Should().Equal(4, 5);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Range_clips_instead_of_failing_when_the_asked_ordinal_was_evicted() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-range-evicted-token");
        WebApplication app = Program.BuildApp([], port, token);
        SeedFiveFrames(app, "frames-range-evicted");
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames?from=-40&count=10");
            using JsonDocument doc = JsonDocument.Parse(body);

            Ordinals(doc).Should().Equal(1, 2, 3, 4, 5);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Range_is_empty_before_any_frame_seals() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-range-empty-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames");
            using JsonDocument doc = JsonDocument.Parse(body);

            doc.RootElement.GetProperty("frames").GetArrayLength().Should().Be(0);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_carries_thread_ids_per_node_and_names_the_lanes() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-threads-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "frames-threads",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        aggregator.OnThreadRegistrations(new ThreadRegistrationsBatch {
            ThreadIds = [1, 7],
            Names = ["Main", "Pathfinder"],
            Roles = [1, 2],
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20, 10],
            ParentIds = [-1, 10, -1],
            StartTimestamps = [100L, 150L, 700L],
            ElapsedTicks = [500L, 200L, 400L],
            FrameOrdinals = [1, 1, 2],
            ThreadIds = [1, 7, 1],
        });
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);

            JsonElement threadIds = doc.RootElement.GetProperty("frame").GetProperty("nodes").GetProperty("thread_ids");
            threadIds.EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1, 7);

            JsonElement[] threads = [.. doc.RootElement.GetProperty("threads").EnumerateArray()];
            threads.Length.Should().Be(2);
            JsonElement main = threads.Single(t => t.GetProperty("id").GetInt32() == 1);
            main.GetProperty("name").GetString().Should().Be("Main");
            main.GetProperty("role").GetInt32().Should().Be(1);
            // 900 ticks at 10 MHz, so 90 microseconds of busy time on the main lane.
            main.GetProperty("busy_ns").GetInt64().Should().Be(90_000L);
            JsonElement pathfinder = threads.Single(t => t.GetProperty("id").GetInt32() == 7);
            pathfinder.GetProperty("name").GetString().Should().Be("Pathfinder");
            pathfinder.GetProperty("role").GetInt32().Should().Be(2);
            pathfinder.GetProperty("busy_ns").GetInt64().Should().Be(20_000L);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Latest_serves_empty_thread_ids_for_a_v8_batch() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("frames-threads-v8-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "frames-threads-v8",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20, 10],
            ParentIds = [-1, 10, -1],
            StartTimestamps = [100L, 150L, 700L],
            ElapsedTicks = [500L, 200L, 400L],
            FrameOrdinals = [1, 1, 2],
        });
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/frames/latest");
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement frame = doc.RootElement.GetProperty("frame");

            frame.GetProperty("node_count").GetInt32().Should().Be(2);
            frame.GetProperty("nodes").GetProperty("thread_ids").GetArrayLength().Should().Be(0);
            doc.RootElement.GetProperty("threads").GetArrayLength().Should().Be(0);
        }
        finally {
            await app.StopAsync();
        }
    }
}
