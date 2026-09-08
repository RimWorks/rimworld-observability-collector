using System.IO;
using System.Net;
using System.Net.Sockets;

namespace RimWorks.RimObs.Transport;

internal static class CollectorPort {
    public const int FirstPort = 25950;

    public static int Allocate() {
        for (int port = FirstPort; port <= 65535; port++) {
            if (IsFree(port))
                return port;
        }

        throw new IOException($"no free loopback port at or above {FirstPort}");
    }

    // the collector binds HTTP and UDP to the same number, so a port free on one and taken on
    // the other is no good to us.
    private static bool IsFree(int port) {
        try {
            TcpListener tcp = new TcpListener(IPAddress.Loopback, port);
            tcp.Start();
            tcp.Stop();
        }
        catch (SocketException) {
            return false;
        }

        try {
            using UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException) {
            return false;
        }
    }
}
