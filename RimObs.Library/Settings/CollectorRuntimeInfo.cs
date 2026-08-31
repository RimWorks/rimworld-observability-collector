namespace RimWorks.RimObs.Settings;

public static class CollectorRuntimeInfo {
    public static string Host { get; private set; } = "127.0.0.1";
    public static int Port { get; private set; }
    public static bool CollectorRunning { get; private set; }
    public static bool LaunchAttempted { get; private set; }
    public static string OwnerId { get; private set; } = string.Empty;

    public static void Set(string host, int port, bool collectorRunning, bool launchAttempted, string ownerId) {
        Host = string.IsNullOrEmpty(host) ? "127.0.0.1" : host;
        Port = port;
        CollectorRunning = collectorRunning;
        LaunchAttempted = launchAttempted;
        OwnerId = ownerId ?? string.Empty;
    }

    internal static void ResetForTests() {
        Host = "127.0.0.1";
        Port = 0;
        CollectorRunning = false;
        LaunchAttempted = false;
        OwnerId = string.Empty;
    }
}
