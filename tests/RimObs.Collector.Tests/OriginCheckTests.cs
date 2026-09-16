using System.Linq;
using RimWorks.RimObs.Collector.Security;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class OriginCheckTests {
    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("post", true)]
    [InlineData("Patch", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("", false)]
    public void RequiresCheck_returns_true_only_for_state_changing_methods(string method, bool expected) {
        OriginCheck.RequiresCheck(method).Should().Be(expected);
    }

    [Theory]
    [InlineData("POST", true, true)]
    [InlineData("POST", false, false)]
    [InlineData("DELETE", false, false)]
    [InlineData("GET", true, false)]
    [InlineData("GET", false, false)]
    public void ShouldEnforce_only_when_csrf_enabled_and_method_mutating(string method, bool csrfEnabled, bool expected) {
        OriginCheck.ShouldEnforce(method, csrfEnabled).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://127.0.0.1:17654", 17654, true)]
    [InlineData("http://localhost:17654", 17654, true)]
    [InlineData("HTTP://127.0.0.1:17654", 17654, true)]
    [InlineData("http://0.0.0.0:17654", 17654, true)]
    [InlineData("http://[::1]:17654", 17654, true)]
    [InlineData("http://127.0.0.1:17654/", 17654, false)]
    [InlineData("http://127.0.0.1:17655", 17654, false)]
    [InlineData("https://127.0.0.1:17654", 17654, false)]
    [InlineData("http://evil.example.com", 17654, false)]
    [InlineData("http://evil.example.com:17654", 17654, false)]
    [InlineData("", 17654, false)]
    [InlineData(null, 17654, false)]
    public void IsAllowedOrigin_accepts_this_machines_names_with_matching_port(string? origin, int port, bool expected) {
        OriginCheck.IsAllowedOrigin(origin, port).Should().Be(expected);
    }

    // a lan or tailscale browser speaks to this machine by one of its own addresses; the scan
    // covers those, and a name the scan cannot know rides the config list.
    [Fact]
    public void IsAllowedOrigin_accepts_a_local_interface_address() {
        string? local = System.Net.NetworkInformation.NetworkInterface
            .GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address)
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(a => a.ToString())
            .FirstOrDefault(a => a != "127.0.0.1");
        if (local is null)
            return;

        OriginCheck.IsAllowedOrigin($"http://{local}:17654", 17654).Should().BeTrue();
    }

    [Fact]
    public void IsAllowedOrigin_accepts_a_configured_extra_origin_verbatim() {
        string[] extras = ["http://sovereign.tail1234.ts.net:17654"];

        OriginCheck.IsAllowedOrigin("http://sovereign.tail1234.ts.net:17654", 17654, extras).Should().BeTrue();
        OriginCheck.IsAllowedOrigin("http://other.tail1234.ts.net:17654", 17654, extras).Should().BeFalse();
    }
}
