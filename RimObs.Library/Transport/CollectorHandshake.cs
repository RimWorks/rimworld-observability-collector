using System;
using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Transport;

public static class CollectorHandshake {
    public static PongMessage? TryPing(string host, int port, string ownerId, TimeSpan timeout) {
        if (string.IsNullOrEmpty(host))
            throw new ArgumentException("host must be provided", nameof(host));
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        byte[] datagram = BuildPingEnvelope(ownerId);

        using (UdpClient client = new UdpClient(AddressFamily.InterNetwork)) {
            client.Client.ReceiveTimeout = (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
            try {
                client.Connect(host, port);
                client.Send(datagram, datagram.Length);

                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] response = client.Receive(ref remote);
                return ParsePong(response);
            }
            catch (SocketException) {
                return null;
            }
        }
    }

    internal static byte[] BuildPingEnvelope(string ownerId) {
        string owner = ownerId ?? string.Empty;
        PingMessage ping = new PingMessage {
            OwnerId = owner,
            SentAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        TelemetryBatch envelope = new TelemetryBatch {
            SchemaVersion = SchemaVersion.Current,
            Sequence = 0,
            OwnerId = owner,
            BatchType = BatchType.Ping,
            Payload = WireCodec.Serialize(ping),
        };
        return WireCodec.Serialize(envelope);
    }

    internal static PongMessage? ParsePong(byte[] datagram) {
        try {
            TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(datagram);
            if (envelope.BatchType != BatchType.Pong)
                return null;
            return WireCodec.Deserialize<PongMessage>(envelope.Payload);
        }
        catch (WireFormatException) {
            return null;
        }
    }
}
