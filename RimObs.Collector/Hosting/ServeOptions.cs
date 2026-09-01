namespace RimWorks.RimObs.Collector.Hosting;

public sealed class ServeOptions {
    public int Port { get; }
    public int ParentPid { get; }
    public bool NoBrowser { get; }

    public ServeOptions(int port, int parentPid, bool noBrowser) {
        Port = port;
        ParentPid = parentPid;
        NoBrowser = noBrowser;
    }

    public static ServeOptions Parse(string[] args, int defaultPort) {
        int port = defaultPort;
        int parentPid = 0;
        bool noBrowser = false;

        int i = 0;
        while (i < args.Length) {
            int consumed = 1;
            switch (args[i]) {
                case "--port":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort) && parsedPort > 0 && parsedPort <= 65535) {
                        port = parsedPort;
                        consumed = 2;
                    }
                    break;
                case "--parent-pid":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPid) && parsedPid > 0) {
                        parentPid = parsedPid;
                        consumed = 2;
                    }
                    break;
                case "--no-browser":
                    noBrowser = true;
                    break;
            }
            i += consumed;
        }

        return new ServeOptions(port, parentPid, noBrowser);
    }
}
