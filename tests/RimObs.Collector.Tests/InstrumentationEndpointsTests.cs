using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Collector.Tests.Stubs;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

    // the row id and the library's patch id are separate counters. deleting row 2 must unpatch
    // whatever the library called that patch, not live patch 2.
    [Fact]
    public async Task Delete_unpatches_the_live_id_not_the_row_id() {
        int port = PickFreePort();
        using StubControlServer stub = new("s");
        List<int> unpatched = [];
        stub.OnUnpatch = id => {
            unpatched.Add(id);
            return true;
        };
        stub.Start();

        RimWorks.RimObs.Collector.Security.CollectorToken token =
            RimWorks.RimObs.Collector.Security.CollectorToken.FromExplicitValue("test-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            app.Services.GetRequiredService<SessionMetaRegistry>().OnSessionMeta(new SessionMeta {
                SessionId = "s1",
                ControlPort = stub.Port,
                ControlSecret = "s",
            });
            DynamicPatchStore store = app.Services.GetRequiredService<DynamicPatchStore>();
            store.Insert("A.B", "First", "");
            long second = store.Insert("C.D", "Second", "");
            store.UpdateLivePatchId(second, 1);

            using HttpClient http = new() { BaseAddress = new System.Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{port}");
            http.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
            HttpResponseMessage res = await http.DeleteAsync($"/api/v1/instrumentation/patches/{second}");

            res.StatusCode.Should().Be(HttpStatusCode.NoContent);
            unpatched.Should().Equal(1);
            store.Find(second).Should().BeNull();
        }
        finally {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Delete_skips_the_control_call_when_the_row_was_never_applied() {
        int port = PickFreePort();
        using StubControlServer stub = new("s");
        List<int> unpatched = [];
        stub.OnUnpatch = id => {
            unpatched.Add(id);
            return true;
        };
        stub.Start();

        RimWorks.RimObs.Collector.Security.CollectorToken token =
            RimWorks.RimObs.Collector.Security.CollectorToken.FromExplicitValue("test-token");
        WebApplication app = Program.BuildApp([], port, token);
        await app.StartAsync();
        try {
            app.Services.GetRequiredService<SessionMetaRegistry>().OnSessionMeta(new SessionMeta {
                SessionId = "s1",
                ControlPort = stub.Port,
                ControlSecret = "s",
            });
            DynamicPatchStore store = app.Services.GetRequiredService<DynamicPatchStore>();
            long row = store.Insert("A.B", "NeverApplied", "");

            using HttpClient http = new() { BaseAddress = new System.Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Add("Origin", $"http://127.0.0.1:{port}");
            http.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
            await http.DeleteAsync($"/api/v1/instrumentation/patches/{row}");

            unpatched.Should().BeEmpty();
            store.Find(row).Should().BeNull();
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
