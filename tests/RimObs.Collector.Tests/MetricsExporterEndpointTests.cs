using System.Net;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class MetricsExporterEndpointTests {
    private static int PickFreePort() {
        System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static ConfigStore InMemoryConfig(bool prometheusEnabled) {
        ConfigStore store = new(configFilePath: null);
        RimObsConfig config = new();
        config.Exporters.PrometheusEnabled = prometheusEnabled;
        store.Replace(config);
        return store;
    }

    [Fact]
    public async Task Metrics_returns_404_when_exporter_disabled() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port, configStore: InMemoryConfig(prometheusEnabled: false));
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            HttpResponseMessage res = await http.GetAsync("/metrics");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Metrics_returns_prometheus_text_when_enabled() {
        int port = PickFreePort();
        WebApplication app = Program.BuildApp([], port, configStore: InMemoryConfig(prometheusEnabled: true));
        await app.StartAsync();
        try {
            using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            HttpResponseMessage res = await http.GetAsync("/metrics");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            res.Content.Headers.ContentType!.ToString().Should().StartWith("text/plain");
            res.Content.Headers.ContentType!.ToString().Should().Contain("version=0.0.4");

            string body = await res.Content.ReadAsStringAsync();
            body.Should().Contain("# TYPE rimobs_collector_connected gauge");
            body.Should().Contain("rimobs_collector_connected 0");
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
