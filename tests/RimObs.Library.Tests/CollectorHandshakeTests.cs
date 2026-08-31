using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RimWorks.RimObs.Transport;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorHandshakeTests {
    [Fact]
    public void BuildPingEnvelope_round_trips_to_ping_message_with_owner() {
        byte[] datagram = CollectorHandshake.BuildPingEnvelope("my.mod");

        TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(datagram);
        envelope.BatchType.Should().Be(BatchType.Ping);
        envelope.SchemaVersion.Should().Be(SchemaVersion.Current);
        PingMessage ping = WireCodec.Deserialize<PingMessage>(envelope.Payload);
        ping.OwnerId.Should().Be("my.mod");
        ping.SentAtUtcTicks.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ParsePong_returns_message_for_pong_envelope() {
        byte[] datagram = ReceiverServer.BuildPong("owner", 42, "1.2.3", "sess-1");

        PongMessage? pong = CollectorHandshake.ParsePong(datagram);

        pong.Should().NotBeNull();
        pong!.CollectorVersion.Should().Be("1.2.3");
        pong.SessionId.Should().Be("sess-1");
    }

    [Fact]
    public void ParsePong_returns_null_for_non_pong_envelope() {
        byte[] datagram = CollectorHandshake.BuildPingEnvelope("x");

        CollectorHandshake.ParsePong(datagram).Should().BeNull();
    }

    [Fact]
    public void ParsePong_returns_null_for_garbage_bytes() {
        CollectorHandshake.ParsePong(new byte[] { 0xff, 0x00, 0x13, 0x37 }).Should().BeNull();
    }

    [Fact]
    public void TryPing_receives_pong_from_live_listener() {
        using ReceiverServer server = new(collectorVersion: "9.9.9", sessionId: "live");

        PongMessage? pong = CollectorHandshake.TryPing("127.0.0.1", server.Port, "the.owner", TimeSpan.FromSeconds(2));

        pong.Should().NotBeNull();
        pong!.OwnerId.Should().Be("the.owner");
        pong.CollectorVersion.Should().Be("9.9.9");
        pong.SessionId.Should().Be("live");
    }


    [Fact]
    public void Receiver_survives_malformed_datagram_and_still_answers() {
        using ReceiverServer server = new(collectorVersion: "9.9.9", sessionId: "resilient");

        using (UdpClient garbage = new()) {
            byte[] junk = { 0xdc };
            garbage.Send(junk, junk.Length, "127.0.0.1", server.Port);
        }

        PongMessage? pong = CollectorHandshake.TryPing("127.0.0.1", server.Port, "the.owner", TimeSpan.FromSeconds(2));

        pong.Should().NotBeNull("a malformed datagram must be skipped, not crash the receiver loop");
        pong!.OwnerId.Should().Be("the.owner");
    }

    [Fact]
    public void TryPing_returns_null_when_no_listener_responds() {
        int deadPort = GetFreePort();

        PongMessage? pong = CollectorHandshake.TryPing("127.0.0.1", deadPort, "owner", TimeSpan.FromMilliseconds(300));

        pong.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void TryPing_rejects_out_of_range_port(int port) {
        Action act = () => CollectorHandshake.TryPing("127.0.0.1", port, "owner", TimeSpan.FromSeconds(1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static int GetFreePort() {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private sealed class ReceiverServer : IDisposable {
        private readonly UdpClient _client;
        private readonly Thread _thread;
        private readonly string _version;
        private readonly string? _sessionId;
        private volatile bool _running = true;

        public ReceiverServer(string collectorVersion, string? sessionId) {
            _version = collectorVersion;
            _sessionId = sessionId;
            _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Port = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public int Port { get; }

        private void Loop() {
            _client.Client.ReceiveTimeout = 200;
            while (_running) {
                IPEndPoint remote = new(IPAddress.Any, 0);
                byte[] datagram;
                try {
                    datagram = _client.Receive(ref remote);
                }
                catch (SocketException) {
                    continue;
                }
                catch (ObjectDisposedException) {
                    break;
                }

                try {
                    TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(datagram);
                    if (envelope.BatchType != BatchType.Ping)
                        continue;
                    PingMessage ping = WireCodec.Deserialize<PingMessage>(envelope.Payload);
                    byte[] pong = BuildPong(ping.OwnerId, ping.SentAtUtcTicks, _version, _sessionId);
                    _client.Send(pong, pong.Length, remote);
                }
                catch (WireFormatException) {
                    continue;
                }
            }
        }

        public static byte[] BuildPong(string ownerId, long pingTicks, string version, string? sessionId) {
            PongMessage pong = new() {
                OwnerId = ownerId,
                PingSentAtUtcTicks = pingTicks,
                CollectorVersion = version,
                SessionId = sessionId,
            };
            TelemetryBatch envelope = new() {
                SchemaVersion = SchemaVersion.Current,
                Sequence = 0,
                OwnerId = "collector",
                BatchType = BatchType.Pong,
                Payload = WireCodec.Serialize(pong),
            };
            return WireCodec.Serialize(envelope);
        }

        public void Dispose() {
            _running = false;
            _client.Dispose();
            _thread.Join(TimeSpan.FromSeconds(1));
        }
    }
}
