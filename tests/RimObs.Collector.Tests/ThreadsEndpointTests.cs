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

public sealed class ThreadsEndpointTests {
    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Current_threads_returns_the_live_lane_table() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("threads-test-token");
        WebApplication app = Program.BuildApp([], port, token);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "threads-endpoint",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
        });
        aggregator.OnThreadRegistrations(new ThreadRegistrationsBatch {
            ThreadIds = [3],
            Names = ["unity-job-2"],
            Roles = [(int)ThreadRole.UnityJob],
        });
        aggregator.Threads.AddBusy(3, 1_000L);
        await app.StartAsync();

        try {
            using HttpClient client = new();
            string body = await client.GetStringAsync($"http://127.0.0.1:{port}/api/v1/sessions/current/threads");
            using JsonDocument doc = JsonDocument.Parse(body);

            doc.RootElement.GetProperty("session_id").GetString().Should().Be("threads-endpoint");
            JsonElement lane = doc.RootElement.GetProperty("threads")[0];
            lane.GetProperty("id").GetInt32().Should().Be(3);
            lane.GetProperty("name").GetString().Should().Be("unity-job-2");
            lane.GetProperty("role").GetInt32().Should().Be((int)ThreadRole.UnityJob);
            lane.GetProperty("busy_ns").GetInt64().Should().Be(100_000L);
        }
        finally {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Unknown_session_id_is_a_404() {
        int port = PickFreePort();
        CollectorToken token = CollectorToken.FromExplicitValue("threads-404-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();

        try {
            using HttpClient client = new();
            HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/sessions/nope/threads");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
        }
    }
}
