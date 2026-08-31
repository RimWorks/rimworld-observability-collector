using System.Net;
using System.Net.Sockets;

namespace RimWorks.RimObs.Transport;

internal static class EphemeralPort {
    public static int Allocate() {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally {
            listener.Stop();
        }
    }
}
