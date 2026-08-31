using System;
using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Session;
using RimWorks.RimObs.Transport;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Control;

public sealed class ControlServerHandshakeTests : IDisposable {
    public void Dispose() {
        ControlServices.ResetForTests();
    }

    private static int GetFreePort() {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public void SessionMeta_advertises_live_control_port_and_secret() {
        int receiverPort = GetFreePort();
        SessionAnchor.Initialize("handshake-session");
        ControlServices.StartServer("test.pkg");
        ControlServices.Server.Should().NotBeNull("StartServer must bind the control server on a free loopback port");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, receiverPort));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.pkg", port: receiverPort);
        sink.Start();

        SessionMeta? meta = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && meta is null) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.SessionMeta)
                    meta = WireCodec.Deserialize<SessionMeta>(envelope.Payload);
            }
            catch (SocketException) {
                break;
            }
        }

        meta.Should().NotBeNull("UdpTelemetrySink should emit a SessionMeta frame within 3s");
        meta!.ControlPort.Should().Be(ControlServices.Server!.Port);
        meta.ControlPort.Should().NotBe(0);
        meta.ControlSecret.Should().Be(ControlServices.Server!.Secret);
        meta.ControlSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SessionMeta_advertises_no_control_server_when_unstarted() {
        int receiverPort = GetFreePort();
        SessionAnchor.Initialize("handshake-session-no-server");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, receiverPort));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.pkg", port: receiverPort);
        sink.Start();

        SessionMeta? meta = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && meta is null) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.SessionMeta)
                    meta = WireCodec.Deserialize<SessionMeta>(envelope.Payload);
            }
            catch (SocketException) {
                break;
            }
        }

        meta.Should().NotBeNull("UdpTelemetrySink should emit a SessionMeta frame within 3s");
        meta!.ControlPort.Should().Be(0);
        meta.ControlSecret.Should().BeEmpty();
    }
}
