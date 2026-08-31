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

    public static bool IsAllowedOrigin(string? origin, int port) {
        if (string.IsNullOrEmpty(origin))
            return false;
        string loopback = $"http://127.0.0.1:{port}";
        string localhost = $"http://localhost:{port}";
        return string.Equals(origin, loopback, StringComparison.OrdinalIgnoreCase)
            || string.Equals(origin, localhost, StringComparison.OrdinalIgnoreCase);
    }
}
