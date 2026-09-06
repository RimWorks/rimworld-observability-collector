using System;
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
}
