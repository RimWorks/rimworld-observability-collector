using System.Diagnostics;
using Serilog;

namespace RimWorks.RimObs.Collector.Hosting;

public static class BrowserLauncher {
    public static void Open(string url) {
        try {
            (string fileName, IReadOnlyList<string> prefixArgs) = BrowserCommand.Resolve(CurrentPlatform(), Environment.GetEnvironmentVariable("BROWSER"));
            ProcessStartInfo psi = new ProcessStartInfo(fileName) {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in prefixArgs)
                psi.ArgumentList.Add(arg);
            psi.ArgumentList.Add(url);
            Process.Start(psi);
            Log.Information("Opened dashboard in browser at {Url}", url);
        }
        catch (Exception ex) {
            Log.Warning(ex, "Failed to auto-open browser at {Url}", url);
        }
    }

    private static BrowserPlatform CurrentPlatform() {
        if (OperatingSystem.IsWindows())
            return BrowserPlatform.Windows;
        if (OperatingSystem.IsMacOS())
            return BrowserPlatform.MacOS;
        return BrowserPlatform.Linux;
    }
}
