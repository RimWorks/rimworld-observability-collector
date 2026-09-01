namespace RimWorks.RimObs.Library.Control;

internal static class ControlServices {
    private static ControlOpQueue? s_Queue;
    private static ControlServer? s_Server;

    public static ControlOpQueue Queue => s_Queue ??= new ControlOpQueue();
    public static ControlServer? Server => s_Server;

    public static void StartServer(string frameworkPackageId) {
        if (s_Server is not null) return;
        byte[] bytes = new byte[32];
        using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
        string secret = System.Convert.ToBase64String(bytes);
        ControlServer server = new ControlServer(secret, frameworkPackageId);
        try {
            server.Start();
        }
        catch (System.Net.HttpListenerException) {
            // bind failure must not kill bootstrap. server stays null, SessionMeta advertises port=0,
            // and the collector's ControlClient reads that as no control server.
            return;
        }
        s_Server = server;
    }

    internal static void ResetForTests() {
        s_Server?.Stop();
        s_Server = null;
        s_Queue = null;
    }
}
