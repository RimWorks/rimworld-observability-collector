namespace RimWorks.RimObs.Collector.Hosting;

public static class BrowserCommand {
    public static (string FileName, IReadOnlyList<string> PrefixArgs) Resolve(BrowserPlatform platform, string? browserEnv) {
        switch (platform) {
            case BrowserPlatform.MacOS:
                return ("open", []);
            case BrowserPlatform.Windows:
                return ("cmd", ["/c", "start", string.Empty]);
            default:
                if (!string.IsNullOrWhiteSpace(browserEnv))
                    return (browserEnv, []);
                return ("xdg-open", []);
        }
    }
}
