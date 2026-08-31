using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Collector.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class InstrumentationEndpointsTests {
    [Fact]
    public async Task Search_returns_503_when_control_port_is_zero() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port);
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new System.Uri($"http://127.0.0.1:{port}") };
            HttpResponseMessage res = await http.GetAsync("/api/v1/instrumentation/search?q=foo&limit=5");
            res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static int PickFreePort() {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
