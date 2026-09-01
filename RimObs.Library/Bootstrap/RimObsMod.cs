using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Config;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Session;
using RimWorks.RimObs.Settings;
using RimWorks.RimObs.Transport;
using Verse;

namespace RimWorks.RimObs.Bootstrap;

public sealed class RimObsMod : Mod {
    private const string FrameworkOwnerId = "RimWorks.RimObs";
    private const string CollectorHost = "127.0.0.1";
    // Cold-launching a fresh self-contained collector (extract + JIT) takes longer than
    // pinging an already-running daemon did, so allow generous readiness headroom.
    private static readonly TimeSpan s_LaunchTimeout = TimeSpan.FromSeconds(10);
    private static UdpTelemetrySink? s_Sink;
    private static CollectorConfigClient? s_ConfigClient;
    private readonly RimObsSettings _settings;

    private static string ResolveOwnerId(ModContentPack content) =>
        string.IsNullOrEmpty(content?.PackageId) ? FrameworkOwnerId : content!.PackageId;

    private static CollectorLaunchResult EnsureCollectorRunning(string ownerId, int port, int parentPid, bool noBrowser) {
        List<CollectorCandidate> candidates = CollectCandidates();
        return CollectorLauncher.EnsureRunning(new CollectorLaunchRequest {
            Candidates = candidates,
            Host = CollectorHost,
            Port = port,
            OwnerId = ownerId,
            ProbeTimeout = CollectorLauncher.DefaultProbeTimeout,
            LaunchTimeout = s_LaunchTimeout,
            ParentPid = parentPid,
            NoBrowser = noBrowser,
        });
    }

    private static List<CollectorCandidate> CollectCandidates() {
        List<CollectorCandidate> candidates = new();
        foreach (ModContentPack pack in LoadedModManager.RunningModsListForReading) {
            string rootDir = pack.RootDir;
            if (string.IsNullOrEmpty(rootDir))
                continue;
            string collectorDir = Path.Combine(rootDir, CollectorScanner.CollectorDirName);
            if (Directory.Exists(collectorDir)) {
                Log.Message(
                    $"[RimObs] Collector discovery: scanning '{collectorDir}' (mod '{pack.PackageId}')."
                );
            }
            CollectorScanner.ReadCandidates(collectorDir, candidates);
        }

        if (candidates.Count == 0) {
            Log.Warning(
                "[RimObs] Collector discovery found 0 candidates across all running mods' Collector directories. "
                    + "No collector binary could be located to launch."
            );
        }
        else {
            for (int i = 0; i < candidates.Count; i++) {
                CollectorCandidate candidate = candidates[i];
                Log.Message(
                    $"[RimObs] Collector discovery: candidate {i + 1}/{candidates.Count} -> '{candidate.ExecutablePath}' (version {candidate.Version})."
                );
            }
        }

        return candidates;
    }

    private static void StartConfigPoll(string host, int port) {
        if (s_ConfigClient != null)
            return;
        CollectorConfigClient client = new($"http://{host}:{port}");
        client.Start();
        s_ConfigClient = client;
    }

    public RimObsMod(ModContentPack content) : base(content) {
        _settings = GetSettings<RimObsSettings>();
        try {
            SessionAnchor.Initialize(Guid.NewGuid().ToString("N"));
            string ownerId = ResolveOwnerId(content);

            int port = EphemeralPort.Allocate();
            int parentPid = Process.GetCurrentProcess().Id;

            ControlServices.StartServer(ownerId);
            WireTelemetrySink(ownerId, port);
            PopulateOwnerRegistry();
            ProfilingXmlLoader.LoadResult declared = LoadDeclaredProfiling();

            CollectorLaunchResult collector = EnsureCollectorRunning(ownerId, port, parentPid, !_settings.AutoOpenDashboard);
            CollectorRuntimeInfo.Set(CollectorHost, port, collector.IsRunning, collector.LaunchAttempted, ownerId);
            if (!collector.IsRunning) {
                Log.Error(
                    "[RimObs] No collector is running and none could be launched from any installed mod's "
                        + "Collector directory. Telemetry instrumentation is disabled for this session "
                        + $"(no patches installed). launchAttempted={collector.LaunchAttempted}. "
                        + "Install the collector binary to enable profiling. (PRD §35.66)"
                );
                return;
            }

            Log.Message($"[RimObs] Dashboard: {CollectorRuntimeInfo.DashboardUrl}");

            InstrumentationInstall.Schedule(LongEventHandler.ExecuteWhenFinished, () => InstallInstrumentation(declared, port));
        }
        catch (Exception ex) {
            Log.Error($"[RimObs] Bootstrap failed: {ex}");
        }
    }

    public override string SettingsCategory() => "RimWorld Observability";

    public override void DoSettingsWindowContents(UnityEngine.Rect inRect) {
        CollectorStatus status = CollectorStatusProvider.CaptureCurrent();
        SettingsWindow.Draw(inRect, status, _settings);
    }

    // Runs on the main thread once the loading long event finishes, not in the constructor.
    // See InstrumentationInstall for why.
    private static void InstallInstrumentation(ProfilingXmlLoader.LoadResult declared, int port) {
        try {
            PatchInstaller.InstallAll();
            ObservedSectionScanner.ScanResult attrs = LoadObservedSections();
            FrameTickPatches.InstallAll();
            s_Sink?.SetPatchConflicts(HarmonyConflictRecorder.BuildBatch());
            Profiler.SetEnabled(true);
            GcObserverHost.Start();
            TpsFpsObserverHost.Start();
            // AllocationSamplerHost is opt-in and stays inert at bootstrap. Mod authors
            // call AllocationSamplerHost.Start() themselves when they want it (PRD §35.18,
            // §11.2). It is off by default because the GC.GetTotalMemory delta heuristic
            // is a soft cost on every poll.
            StartConfigPoll(CollectorHost, port);
            LogBootstrapSummary(declared, attrs);
        }
        catch (Exception ex) {
            Log.Error($"[RimObs] Instrumentation install failed: {ex}");
        }
    }

    private static void WireTelemetrySink(string ownerId, int port) {
        if (s_Sink != null)
            return;
        UdpTelemetrySink sink = new(ownerId, port);
        sink.Start();
        Profiler.SetSink(sink);
        GcObserverHost.SetSink(sink);
        AllocationSamplerHost.SetSink(sink);
        TpsFpsObserverHost.SetSink(sink);
        s_Sink = sink;
    }

    private static ObservedSectionScanner.ScanResult LoadObservedSections() {
        List<(string, IReadOnlyList<Assembly>)> mods = new List<(string, IReadOnlyList<Assembly>)>();
        foreach (ModContentPack pack in LoadedModManager.RunningModsListForReading) {
            string packageId = pack.PackageId;
            if (string.IsNullOrEmpty(packageId))
                continue;
            List<Assembly> asms = pack.assemblies.loadedAssemblies;
            if (asms == null || asms.Count == 0)
                continue;
            mods.Add((packageId, asms));
        }
        return ObservedSectionScanner.Scan(mods);
    }

    private static void LogBootstrapSummary(ProfilingXmlLoader.LoadResult declared, ObservedSectionScanner.ScanResult attrs) {
        (int coreCount, int coreInstalled, int declaredCount, int declaredInstalled) = CountSections();

        Log.Message(
            $"[RimObs] Loaded. Core: {coreInstalled}/{coreCount} sections installed. "
                + $"Declared: {declaredInstalled}/{declaredCount} sections from {declared.FilesLoaded}/{declared.FilesScanned} profiling.xml files. "
                + $"Attributes: {attrs.Registered} registered ({attrs.SkippedDuplicate} duplicate, {attrs.SkippedUnsupported} unsupported, {attrs.Failed} failed) from {attrs.AssembliesScanned} assemblies. "
                + $"(unresolved={PatchInstaller.UnresolvedCount}, failed={PatchInstaller.FailedCount}, conflicts={HarmonyConflictRecorder.Count}). "
                + $"Owner registry: {OwnerRegistry.Count} mods. GcObserver: maxGen={GcObserverHost.Instance.MaxGeneration}."
        );

        LogLoadWarnings(declared, attrs);
        LogSectionResolutionIssues();
    }

    private static (int CoreCount, int CoreInstalled, int DeclaredCount, int DeclaredInstalled) CountSections() {
        int coreCount = 0;
        int declaredCount = 0;
        int coreInstalled = 0;
        int declaredInstalled = 0;
        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (entry.Declared) {
                declaredCount++;
                if (entry.Installed)
                    declaredInstalled++;
            }
            else {
                coreCount++;
                if (entry.Installed)
                    coreInstalled++;
            }
        }
        return (coreCount, coreInstalled, declaredCount, declaredInstalled);
    }

    private static void LogLoadWarnings(ProfilingXmlLoader.LoadResult declared, ObservedSectionScanner.ScanResult attrs) {
        foreach (string warning in declared.Warnings)
            Log.Warning($"[RimObs] profiling.xml: {warning}");

        foreach (string warning in attrs.Warnings)
            Log.Warning($"[RimObs] [ObservedSection]: {warning}");
    }

    private static void LogSectionResolutionIssues() {
        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (!entry.Installed && entry.ResolutionError != null)
                Log.Warning($"[RimObs] Section '{entry.Name}' unresolved: {entry.ResolutionError.Message}");
            else if (entry.InstallError != null)
                Log.Error($"[RimObs] Section '{entry.Name}' install failed: {entry.InstallError.Message}");
        }
    }

    private static void PopulateOwnerRegistry() {
        foreach (ModContentPack pack in LoadedModManager.RunningModsListForReading) {
            string packageId = pack.PackageId;
            if (string.IsNullOrEmpty(packageId))
                continue;

            foreach (Assembly asm in pack.assemblies.loadedAssemblies) {
                OwnerRegistry.RegisterMod(asm, packageId);
            }
        }

        OwnerRegistry.SetLateResolver(ResolvePackageIdFromLoadedMods);
    }

    private static string? ResolvePackageIdFromLoadedMods(Assembly assembly) {
        if (assembly == null)
            return null;

        List<ModContentPack>? mods = LoadedModManager.RunningModsListForReading;
        if (mods == null)
            return null;

        for (int i = 0; i < mods.Count; i++) {
            ModContentPack pack = mods[i];
            string packageId = pack.PackageId;
            if (string.IsNullOrEmpty(packageId))
                continue;

            List<Assembly> assemblies = pack.assemblies.loadedAssemblies;
            for (int j = 0; j < assemblies.Count; j++) {
                if (ReferenceEquals(assemblies[j], assembly))
                    return packageId;
            }
        }

        return null;
    }

    private static ProfilingXmlLoader.LoadResult LoadDeclaredProfiling() {
        List<(string, string)> mods = new();
        foreach (ModContentPack pack in LoadedModManager.RunningModsListForReading) {
            string packageId = pack.PackageId;
            if (string.IsNullOrEmpty(packageId))
                continue;
            string rootDir = pack.RootDir;
            if (string.IsNullOrEmpty(rootDir))
                continue;
            mods.Add((rootDir, packageId));
        }
        return ProfilingXmlLoader.LoadFromMods(mods);
    }
}
