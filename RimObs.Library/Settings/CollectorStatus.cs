using System.Collections.Generic;

namespace RimWorks.RimObs.Settings;

public sealed class CollectorStatus {
    public CollectorStatus(
        bool collectorRunning,
        bool launchAttempted,
        string host,
        int port,
        int controlPort,
        bool profilerEnabled,
        int coreInstalled,
        int coreTotal,
        int declaredInstalled,
        int declaredTotal,
        int unresolvedCount,
        int failedCount,
        int ownerCount,
        int conflictCount,
        bool gcObserverRunning,
        bool tpsFpsObserverRunning,
        bool allocationSamplerRunning,
        string sessionId,
        string ownerId) {
        CollectorRunning = collectorRunning;
        LaunchAttempted = launchAttempted;
        Host = string.IsNullOrEmpty(host) ? "127.0.0.1" : host;
        Port = port;
        ControlPort = controlPort;
        ProfilerEnabled = profilerEnabled;
        CoreInstalled = coreInstalled;
        CoreTotal = coreTotal;
        DeclaredInstalled = declaredInstalled;
        DeclaredTotal = declaredTotal;
        UnresolvedCount = unresolvedCount;
        FailedCount = failedCount;
        OwnerCount = ownerCount;
        ConflictCount = conflictCount;
        GcObserverRunning = gcObserverRunning;
        TpsFpsObserverRunning = tpsFpsObserverRunning;
        AllocationSamplerRunning = allocationSamplerRunning;
        SessionId = sessionId ?? string.Empty;
        OwnerId = ownerId ?? string.Empty;
    }

    public bool CollectorRunning { get; }
    public bool LaunchAttempted { get; }
    public string Host { get; }
    public int Port { get; }
    public int ControlPort { get; }
    public bool ProfilerEnabled { get; }
    public int CoreInstalled { get; }
    public int CoreTotal { get; }
    public int DeclaredInstalled { get; }
    public int DeclaredTotal { get; }
    public int UnresolvedCount { get; }
    public int FailedCount { get; }
    public int OwnerCount { get; }
    public int ConflictCount { get; }
    public bool GcObserverRunning { get; }
    public bool TpsFpsObserverRunning { get; }
    public bool AllocationSamplerRunning { get; }
    public string SessionId { get; }
    public string OwnerId { get; }

    public bool DashboardAvailable => CollectorRunning && Port > 0;
    public string DashboardUrl => Port > 0 ? $"http://{Host}:{Port}/" : string.Empty;

    public IReadOnlyList<StatusLine> BuildLines() {
        List<StatusLine> lines = new(12);

        lines.Add(BuildCollectorLine());
        lines.Add(BuildControlServerLine());
        lines.Add(BuildCoreSectionsLine());
        lines.Add(BuildDeclaredSectionsLine());

        lines.Add(new StatusLine("Profiler", ProfilerEnabled ? "enabled" : "disabled", ProfilerEnabled));
        lines.Add(new StatusLine("Owners registered", OwnerCount.ToString(), OwnerCount > 0));
        lines.Add(new StatusLine("Harmony conflicts", ConflictCount.ToString(), ConflictCount == 0));
        lines.Add(new StatusLine("GC observer", GcObserverRunning ? "running" : "stopped", GcObserverRunning));
        lines.Add(new StatusLine("TPS/FPS observer", TpsFpsObserverRunning ? "running" : "stopped", TpsFpsObserverRunning));
        lines.Add(new StatusLine("Allocation sampler", AllocationSamplerRunning ? "running" : "off (opt-in)", true));

        string sessionDisplay = string.IsNullOrEmpty(SessionId) ? "(uninitialized)" : SessionId;
        lines.Add(new StatusLine("Session", sessionDisplay, !string.IsNullOrEmpty(SessionId)));

        if (!string.IsNullOrEmpty(OwnerId))
            lines.Add(new StatusLine("Owner id", OwnerId, true));

        return lines;
    }

    private StatusLine BuildCollectorLine() {
        if (CollectorRunning)
            return new StatusLine("Collector", $"running on {Host}:{Port}", true);
        if (LaunchAttempted)
            return new StatusLine("Collector", "launch attempted but no response", false);
        return new StatusLine("Collector", "not running", false);
    }

    private StatusLine BuildControlServerLine() {
        if (ControlPort > 0)
            return new StatusLine("Control server", $"bound on {Host}:{ControlPort}", true);
        return new StatusLine("Control server", "not bound (dynamic instrumentation disabled)", false);
    }

    private StatusLine BuildCoreSectionsLine() {
        bool coreHealthy = CoreTotal == 0 || (CoreInstalled == CoreTotal && UnresolvedCount == 0 && FailedCount == 0);
        string coreValue = CoreTotal == 0
            ? "0/0 (not installed)"
            : $"{CoreInstalled}/{CoreTotal} installed (unresolved={UnresolvedCount}, failed={FailedCount})";
        return new StatusLine("Core sections", coreValue, coreHealthy);
    }

    private StatusLine BuildDeclaredSectionsLine() {
        bool declaredHealthy = DeclaredTotal == 0 || DeclaredInstalled == DeclaredTotal;
        string declaredValue = DeclaredTotal == 0
            ? "none"
            : $"{DeclaredInstalled}/{DeclaredTotal} from profiling.xml";
        return new StatusLine("Declared sections", declaredValue, declaredHealthy);
    }
}
