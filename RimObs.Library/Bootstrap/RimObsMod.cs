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
using RimWorks.RimObs.Logging;
using Log = RimWorks.RimLogging.Log;

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
                Log.Info(
                    LogChannels.Collector,
                    "scanning {Dir} for a collector binary, from mod {Mod}",
                    new object?[] { collectorDir, pack.PackageId }
                );
            }
            CollectorScanner.ReadCandidates(collectorDir, candidates);
        }

        if (candidates.Count == 0) {
            Log.Warn(
                LogChannels.Collector,
                "no collector binary found in any running mod's Collector directory"
            );
        }
        else {
            for (int i = 0; i < candidates.Count; i++) {
                CollectorCandidate candidate = candidates[i];
                Log.Info(
                    LogChannels.Collector,
                    "candidate {Index}/{Total}: {Path} version {Version}",
                    new object?[] { i + 1, candidates.Count, candidate.ExecutablePath, candidate.Version }
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
                    LogChannels.Collector,
                    "no collector running and none could be launched, so nothing is instrumented this session",
                    new { launch_attempted = collector.LaunchAttempted, prd = "35.66" }
                );
                return;
            }

            Log.Info(LogChannels.Collector, "dashboard at {Url}", new object?[] { CollectorRuntimeInfo.DashboardUrl });

            InstrumentationInstall.Schedule(LongEventHandler.ExecuteWhenFinished, () => InstallInstrumentation(declared, port));
        }
        catch (Exception ex) {
            Log.Error(LogChannels.Bootstrap, ex, "bootstrap failed");
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
            PatchBackends.SelectBest();
            if (PatchBackends.Active == null) {
                ReportMissingBackend();
                return;
            }

            Log.Info(
                LogChannels.Patching,
                "patching via {Backend} at priority {Priority}",
                new object?[] { PatchBackends.Active.Name, PatchBackends.ActivePriority }
            );
            PatchInstaller.InstallAll();
            ObservedSectionScanner.ScanResult attrs = LoadObservedSections();
            FrameTickPatches.InstallAll();
            s_Sink?.SetPatchConflicts(PatchConflictRecorder.BuildBatch());
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
            Log.Error(LogChannels.Patching, ex, "instrumentation install failed");
        }
    }

    // About.xml lists no patching library as a dependency, so RimWorld raises no
    // missing-dependency warning for one. this replaces it, and is not DevMode-gated
    // because players run this.
    private static void ReportMissingBackend() {
        string? text = MissingBackendNotice.ClaimOnce();
        if (text == null)
            return;

        // Both the error and the dialog wait for the queued event. Find.UIRoot is null until
        // InitializingInterface builds it, and PlayDataLoader.loadedInt is still false until
        // LoadAllPlayData returns - Log.Error force-opens the debug log while it is, which
        // would cover the dialog.
        LongEventHandler.QueueLongEvent(
            () => {
                Log.Error(
                    LogChannels.Patching,
                    "no patching backend loaded, so nothing is instrumented",
                    new { needs = "Harmony or Concord" }
                );
                Find.WindowStack.Add(
                    new Dialog_MessageBox(
                        text,
                        "Close".Translate(),
                        null,
                        null,
                        null,
                        "RimObs",
                        buttonADestructive: false,
                        acceptAction: null,
                        // without a cancelAction closeOnCancel stays false, so Escape does nothing.
                        cancelAction: () => { }
                    )
                );
            },
            null,
            doAsynchronously: false,
            null
        );
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

        Log.Info(
            LogChannels.Bootstrap,
            "loaded",
            new {
                core_installed = coreInstalled,
                core_total = coreCount,
                declared_installed = declaredInstalled,
                declared_total = declaredCount,
                xml_files_loaded = declared.FilesLoaded,
                xml_files_scanned = declared.FilesScanned,
                attrs_registered = attrs.Registered,
                attrs_duplicate = attrs.SkippedDuplicate,
                attrs_unsupported = attrs.SkippedUnsupported,
                attrs_failed = attrs.Failed,
                assemblies_scanned = attrs.AssembliesScanned,
                unresolved = PatchInstaller.UnresolvedCount,
                install_failed = PatchInstaller.FailedCount,
                conflicts = PatchConflictRecorder.Count,
                owner_mods = OwnerRegistry.Count,
                gc_max_generation = GcObserverHost.Instance.MaxGeneration,
            }
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
            Log.Warn(LogChannels.Sections, "profiling.xml: {Warning}", new object?[] { warning });

        foreach (string warning in attrs.Warnings)
            Log.Warn(LogChannels.Sections, "[ObservedSection]: {Warning}", new object?[] { warning });
    }

    private static void LogSectionResolutionIssues() {
        foreach (CatalogEntry entry in SectionCatalog.Entries) {
            if (!entry.Installed && entry.ResolutionError != null)
                Log.Warn(
                    LogChannels.Sections,
                    "section {Section} unresolved: {Reason}",
                    new object?[] { entry.Name, entry.ResolutionError.Message }
                );
            else if (entry.InstallError != null)
                Log.Error(
                    LogChannels.Sections,
                    "section {Section} install failed: {Reason}",
                    new object?[] { entry.Name, entry.InstallError.Message }
                );
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
