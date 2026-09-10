using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace RimWorks.RimObs.Collector.Hosting;

public static class BrowserLauncher {
    /// <summary>errno ENOENT: the browser binary is not on this machine.</summary>
    private const int NotFound = 2;

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
        // a container has no browser to launch, which is not a fault worth a stack trace. the
        // url is already in the log above, so a reader can still open it by hand.
#pragma warning disable S6667 // passing the exception is the bug: a container with no browser is expected, and its stack trace reads as a crash.
        catch (Win32Exception ex) when (ex.NativeErrorCode == NotFound) {
            Log.Information("No browser to open. The dashboard is at {Url}", url);
        }
#pragma warning restore S6667
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
