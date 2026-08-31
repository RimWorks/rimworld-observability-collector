using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Transport;

public sealed class CollectorLaunchResult {
    public CollectorLaunchResult(bool isRunning, PongMessage? pong, CollectorCandidate? selected, bool launchAttempted, Exception? launchError = null) {
        IsRunning = isRunning;
        Pong = pong;
        SelectedCandidate = selected;
        LaunchAttempted = launchAttempted;
        LaunchError = launchError;
    }

    public bool IsRunning { get; }
    public PongMessage? Pong { get; }
    public CollectorCandidate? SelectedCandidate { get; }
    public bool LaunchAttempted { get; }
    public Exception? LaunchError { get; }
}

public static class CollectorLauncher {
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultLaunchTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    public static CollectorLaunchResult EnsureRunning(
        IEnumerable<CollectorCandidate> candidates,
        string host,
        int port,
        string ownerId,
        TimeSpan probeTimeout,
        TimeSpan launchTimeout,
        Action<CollectorCandidate>? launchAction = null,
        int parentPid = 0,
        bool noBrowser = false) {
        if (string.IsNullOrEmpty(host))
            throw new ArgumentException("host must be provided", nameof(host));
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (probeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(probeTimeout));
        if (launchTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(launchTimeout));

        PongMessage? pong = CollectorHandshake.TryPing(host, port, ownerId, probeTimeout);
        if (pong != null)
            return new CollectorLaunchResult(true, pong, null, false);

        CollectorCandidate? best = CollectorDiscovery.SelectHighest(candidates);
        if (best is null)
            return new CollectorLaunchResult(false, null, null, false);

        Action<CollectorCandidate> launch = launchAction ?? (candidate => DefaultLaunch(candidate, port, parentPid, noBrowser));
        try {
            launch(best);
        }
        catch (Exception ex) {
            return new CollectorLaunchResult(false, null, best, true, ex);
        }

        DateTime deadline = DateTime.UtcNow + launchTimeout;
        while (DateTime.UtcNow < deadline) {
            pong = CollectorHandshake.TryPing(host, port, ownerId, probeTimeout);
            if (pong != null)
                return new CollectorLaunchResult(true, pong, best, true);
            Thread.Sleep((int)PollInterval.TotalMilliseconds);
        }

        return new CollectorLaunchResult(false, null, best, true);
    }

    public static string BuildLaunchArguments(int port, int parentPid, bool noBrowser = false) {
        string args = $"serve --port {port}";
        if (parentPid > 0)
            args += $" --parent-pid {parentPid}";
        if (noBrowser)
            args += " --no-browser";
        return args;
    }

    private static void DefaultLaunch(CollectorCandidate candidate, int port, int parentPid, bool noBrowser) {
        ProcessStartInfo psi = new ProcessStartInfo(candidate.ExecutablePath, BuildLaunchArguments(port, parentPid, noBrowser)) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        Process.Start(psi);
    }
}
