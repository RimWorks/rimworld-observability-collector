using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorPortTests {
    [Fact]
    public void Allocate_starts_at_the_first_port() {
        int port = CollectorPort.Allocate();
        port.Should().BeGreaterThanOrEqualTo(CollectorPort.FirstPort);
        port.Should().BeLessThanOrEqualTo(65535);
    }

    [Fact]
    public void Allocate_skips_a_port_already_bound_on_tcp() {
        int taken = CollectorPort.Allocate();
        TcpListener listener = new TcpListener(IPAddress.Loopback, taken);
        listener.Start();
        try {
            CollectorPort.Allocate().Should().BeGreaterThan(taken);
        }
        finally {
            listener.Stop();
        }
    }

    [Fact]
    public void Allocate_skips_a_port_already_bound_on_udp() {
        int taken = CollectorPort.Allocate();
        using UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, taken));
        CollectorPort.Allocate().Should().BeGreaterThan(taken);
    }
}
