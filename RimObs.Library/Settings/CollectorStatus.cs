using System.Collections.Generic;

namespace RimWorks.RimObs.Settings;

public sealed class CollectorStatus {
    private readonly string _host = "127.0.0.1";
    private readonly string _sessionId = string.Empty;
    private readonly string _ownerId = string.Empty;
    private readonly string _patchBackend = string.Empty;

    public bool CollectorRunning { get; init; }
    public bool LaunchAttempted { get; init; }

    public string Host {
        get => _host;
        init => _host = string.IsNullOrEmpty(value) ? "127.0.0.1" : value;
    }

    public int Port { get; init; }
    public int ControlPort { get; init; }
    public bool ProfilerEnabled { get; init; }
    public int CoreInstalled { get; init; }
    public int CoreTotal { get; init; }
    public int DeclaredInstalled { get; init; }
    public int DeclaredTotal { get; init; }
    public int UnresolvedCount { get; init; }
    public int FailedCount { get; init; }
    public int OwnerCount { get; init; }
    public int ConflictCount { get; init; }

    public string PatchBackend {
        get => _patchBackend;
        init => _patchBackend = value ?? string.Empty;
    }

    public int PatchBackendPriority { get; init; }
    public bool GcObserverRunning { get; init; }
    public bool TpsFpsObserverRunning { get; init; }
    public bool AllocationSamplerRunning { get; init; }

    public string SessionId {
        get => _sessionId;
        init => _sessionId = value ?? string.Empty;
    }

    public string OwnerId {
        get => _ownerId;
        init => _ownerId = value ?? string.Empty;
    }

    public bool DashboardAvailable => CollectorRunning && Port > 0;
    public string DashboardUrl => Port > 0 ? $"http://{Host}:{Port}/" : string.Empty;

    public IReadOnlyList<StatusLine> BuildLines() {
        List<StatusLine> lines = new(12);

        lines.Add(BuildCollectorLine());
        lines.Add(BuildControlServerLine());
        lines.Add(BuildPatchBackendLine());
        lines.Add(BuildCoreSectionsLine());
        lines.Add(BuildDeclaredSectionsLine());

        lines.Add(new StatusLine("Profiler", ProfilerEnabled ? "enabled" : "disabled", ProfilerEnabled));
        lines.Add(new StatusLine("Owners registered", OwnerCount.ToString(), OwnerCount > 0));
        lines.Add(new StatusLine("Patch conflicts", ConflictCount.ToString(), ConflictCount == 0));
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

    private StatusLine BuildPatchBackendLine() {
        if (string.IsNullOrEmpty(PatchBackend))
            return new StatusLine("Patch backend", "none (install Harmony or Concord)", false);
        return new StatusLine("Patch backend", $"{PatchBackend} (priority {PatchBackendPriority})", true);
    }

    // 0/0 is the no-backend state: PatchInstaller.InstallAll bails before registering the core
    // pack, so nothing green should suggest the sections are fine.
    private StatusLine BuildCoreSectionsLine() {
        bool coreHealthy = CoreTotal > 0 && CoreInstalled == CoreTotal && UnresolvedCount == 0 && FailedCount == 0;
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
