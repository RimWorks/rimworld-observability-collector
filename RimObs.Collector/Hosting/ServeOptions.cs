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

        for (int i = 0; i < args.Length; i++) {
            switch (args[i]) {
                case "--port":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort) && parsedPort > 0 && parsedPort <= 65535) {
                        port = parsedPort;
                        i++;
                    }
                    break;
                case "--parent-pid":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPid) && parsedPid > 0) {
                        parentPid = parsedPid;
                        i++;
                    }
                    break;
                case "--no-browser":
                    noBrowser = true;
                    break;
            }
        }

        return new ServeOptions(port, parentPid, noBrowser);
    }
}
