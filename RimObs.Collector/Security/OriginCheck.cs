using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace RimWorks.RimObs.Collector.Security;

internal static class OriginCheck {
    public static bool ShouldEnforce(string method, bool csrfEnabled) {
        return csrfEnabled && RequiresCheck(method);
    }

    public static bool RequiresCheck(string method) {
        if (string.IsNullOrEmpty(method))
            return false;
        return method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            || method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
            || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    // the machine's own addresses, so tailscale and lan spellings of this host pass while a
    // foreign site's origin never can. computed once: interfaces do not change mid-run for us.
    private static readonly Lazy<HashSet<string>> LocalHosts = new(() => {
        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "0.0.0.0", "[::1]" };
        try {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                foreach (IPAddress addr in nic.GetIPProperties().UnicastAddresses.Select(ip => ip.Address)) {
                    hosts.Add(addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                        ? $"[{addr}]"
                        : addr.ToString());
                }
            }
        }
        catch (NetworkInformationException) {
            // interface enumeration can fail in sandboxes; loopback spellings still work.
        }
        return hosts;
    });

    public static bool IsAllowedOrigin(string? origin, int port) {
        return IsAllowedOrigin(origin, port, null);
    }

    public static bool IsAllowedOrigin(string? origin, int port, IReadOnlyList<string>? extraOrigins) {
        if (string.IsNullOrEmpty(origin))
            return false;
        if (extraOrigins is not null && extraOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
            return false;
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            return false;
        if (uri.Port != port)
            return false;
        // Uri.Host keeps ipv6 brackets, so it matches the set's bracketed spellings as-is.
        string host = uri.Host;
        if (origin.EndsWith("/", StringComparison.Ordinal))
            return false;
        return LocalHosts.Value.Contains(host);
    }
}
