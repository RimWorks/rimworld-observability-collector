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

public sealed class CapturesEndpointsTests {
    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpRequestMessage Post(int port, CollectorToken token, string path) {
        HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Add("Origin", $"http://127.0.0.1:{port}");
        request.Headers.Add("Authorization", $"Bearer {token.Value}");
        return request;
    }

    [Fact]
    public async Task Start_records_a_slow_tick_tree_then_stop_finalizes() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("capture-test-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
            // Force the CaptureManager to construct so it subscribes to the aggregator.
            _ = app.Services.GetRequiredService<Captures.CaptureManager>();

            aggregator.OnSessionMeta(new SessionMeta {
                SessionId = "cap-endpoint",
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                AnchorTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
            });
            aggregator.OnSectionRegistrations(new SectionRegistrationsBatch {
                SectionIds = [10, 20],
                Names = ["tick.update", "tick.pathfind"],
            });

            HttpResponseMessage startResp = await http.SendAsync(Post(port, token, "/api/v1/captures/start"));
            startResp.StatusCode.Should().Be(HttpStatusCode.OK);

            long freq = System.Diagnostics.Stopwatch.Frequency;
            long slowTickTicks = freq / 20; // ~50ms, well over the 16.67ms threshold.
            aggregator.OnSectionBatch(new SectionBatch {
                SectionIds = [10, 20, 20],
                ParentIds = [-1, 10, 10],
                StartTimestamps = [1, 2, 3],
                ElapsedTicks = [slowTickTicks, slowTickTicks / 3, slowTickTicks / 4],
            });

            string listBody = await http.GetStringAsync("/api/v1/sessions/current/captures?tree=true");
            using JsonDocument list = JsonDocument.Parse(listBody);
            JsonElement captures = list.RootElement.GetProperty("captures");
            captures.GetArrayLength().Should().Be(1);

            JsonElement capture = captures[0];
            capture.GetProperty("status").GetString().Should().Be("running");
            JsonElement roots = capture.GetProperty("roots");
            roots.GetArrayLength().Should().BeGreaterThan(0);
            JsonElement root = roots[0];
            root.GetProperty("name").GetString().Should().Be("tick.update");
            root.GetProperty("total_ns").GetInt64().Should().BeGreaterThan(0);
            root.GetProperty("children").GetArrayLength().Should().Be(1);

            HttpResponseMessage stopResp = await http.SendAsync(Post(port, token, "/api/v1/captures/stop"));
            stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument stopped = JsonDocument.Parse(await stopResp.Content.ReadAsStringAsync());
            stopped.RootElement.GetProperty("capture").GetProperty("status").GetString().Should().Be("finalized");
            stopped.RootElement.GetProperty("capture").GetProperty("finalize_reason").GetString().Should().Be("user_stopped");
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stop_with_no_active_capture_returns_not_found() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("capture-test-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            HttpResponseMessage stopResp = await http.SendAsync(Post(port, token, "/api/v1/captures/stop"));
            stopResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
