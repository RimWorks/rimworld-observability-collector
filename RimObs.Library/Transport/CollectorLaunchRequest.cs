using System;
using System.Collections.Generic;

namespace RimWorks.RimObs.Transport;

public sealed class CollectorLaunchRequest {
    public IEnumerable<CollectorCandidate> Candidates { get; init; } = [];
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public TimeSpan ProbeTimeout { get; init; } = CollectorLauncher.DefaultProbeTimeout;
    public TimeSpan LaunchTimeout { get; init; } = CollectorLauncher.DefaultLaunchTimeout;

    // Test seam. Null means spawn the real process.
    public Action<CollectorCandidate>? LaunchAction { get; init; }

    public int ParentPid { get; init; }
    public bool NoBrowser { get; init; }
}
