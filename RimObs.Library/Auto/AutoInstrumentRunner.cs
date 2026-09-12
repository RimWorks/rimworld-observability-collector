using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Auto;

/// <summary>
/// Applies a scanned plan a slice at a time, on the main thread, under a per-frame time budget.
/// A patch storm that freezes the game is worse than a profile that fills in over a few seconds.
/// </summary>
internal static class AutoInstrumentRunner {
    /// <summary>Wall clock a single frame may spend patching, out of a 16.7 ms budget.</summary>
    public const double BudgetMillis = 4.0;

    private static MethodInfo[]? s_Pending;
    private static string s_OwnerId = string.Empty;
    private static int s_MaxTargets = AutoInstrumentScanner.DefaultMaxTargets;

    // one dedicated patch thread: concord serializes concurrent applies (a pool ran 0.23x),
    // and off-main the pump costs the frame nothing. list mutations stay on the main thread.
    private static Thread? s_Worker;
    private static readonly ManualResetEventSlim s_WorkerStop = new(false);
    private static readonly AutoResetEvent s_WorkArrived = new(false);
    private static readonly object s_WorkGate = new object();
    private static WorkBatch? s_WorkerBatch;
    private static int s_Generation;
    private static int s_InFlight;
    private static readonly ConcurrentQueue<WorkResult> s_Results = new();
    private static MethodPattern[] s_CurrentIncludes = [];
    private static MethodPattern[] s_CurrentExcludes = [];

    private sealed record WorkBatch(MethodInfo[] Targets, string OwnerId, int Generation);

    private sealed record WorkResult(
        MethodInfo Method, int PatchId, int SectionId, bool Active, bool WasInCatalog, int Generation);

    // what this runner patched, so a later filter change can undo its own work and nobody
    // else's. a patch a user applied by hand through the control endpoint never lands here.
    private static readonly List<AppliedPatch> s_Applied = new List<AppliedPatch>();

    // unpatching is budgeted the same as patching, so it queues instead of running inline.
    private static readonly List<int> s_Removing = new List<int>();
    private static int s_RemoveNext;

    // SectionCatalog keeps its method mapping after an unpatch, so the scanner would report a
    // reverted method as already instrumented forever. holding them here lets a re-enable
    // queue them straight back.
    private static readonly HashSet<MethodInfo> s_Reverted = new HashSet<MethodInfo>();

    // the judge mutes on the sender thread; this pump-side watermark makes the sweep run only
    // when something new was muted.
    private static int s_LastMutedSeen;

    /// <summary>
    /// Most methods this runner may keep patched. Changing it re-arms the parked settings, so
    /// the next frame rescans, and lowering it unpatches the newest targets past the new cap.
    /// </summary>
    public static int MaxTargets {
        get => s_MaxTargets;
        set {
            int capped = value <= 0 ? AutoInstrumentScanner.DefaultMaxTargets : value;
            if (capped == s_MaxTargets)
                return;
            s_MaxTargets = capped;
            AutoInstrumentRequest.Rearm();
        }
    }

    public static int Matched { get; private set; }

    /// <summary>Eligible methods the cap left out of the last plan.</summary>
    public static int SkippedOverCap { get; private set; }

    public static bool Truncated => SkippedOverCap > 0;

    public static int SkippedTrivial { get; private set; }

    public static int SkippedOther { get; private set; }

    public static int Instrumented { get; private set; }

    public static int Refused { get; private set; }

    public static int Muted => AutoMute.MutedCount;

    public static int Pending =>
        (s_Pending is null ? 0 : s_Pending.Length)
        + Volatile.Read(ref s_InFlight)
        + s_Results.Count
        + (s_Removing.Count - s_RemoveNext);

    /// <summary>Scans and queues in one call. The scan is blocking; the patching is not.</summary>
    public static AutoInstrumentPlan ApplyFilters(
        string? filters, string? ignore, bool autoMute, string ownerId, IEnumerable<Assembly>? assemblies = null
    ) {
        MethodPattern[] patterns = Combine(filters, ignore);
        MethodPattern.Split(patterns, out MethodPattern[] includes, out MethodPattern[] excludes);
        s_CurrentIncludes = includes;
        s_CurrentExcludes = excludes;

        QueueStaleRemovals(includes, excludes);
        AutoInstrumentPlan plan = AutoInstrumentScanner.Scan(
            assemblies ?? AssemblyIndex.Enumerate(), patterns, s_MaxTargets);
        RestoreReverted(plan, includes, excludes);
        EnforceCap(plan);
        Submit(plan, ownerId, autoMute);
        return plan;
    }

    /// <summary>
    /// Counts what these filters would do without patching anything. Shares Combine and Scan
    /// with <see cref="ApplyFilters"/>, so the number shown can never drift from the number
    /// applied.
    /// </summary>
    public static AutoInstrumentPlan Preview(
        string? filters, string? ignore, IEnumerable<Assembly>? assemblies = null, int maxTargets = 0
    ) {
        MethodPattern[] patterns = Combine(filters, ignore);
        return AutoInstrumentScanner.Scan(
            assemblies ?? AssemblyIndex.Enumerate(), patterns, maxTargets > 0 ? maxTargets : s_MaxTargets);
    }

    /// <summary>Include lines and ignore lines as one list. Ignore lines are forced negative.</summary>
    private static MethodPattern[] Combine(string? filters, string? ignore) {
        MethodPattern[] included = MethodPattern.ParseAll(filters);
        // enabled (non-null) with no positive line means everything: the default filter set
        // ships exclusions only. null stays "off".
        if (filters is not null && !HasPositive(included)) {
            MethodPattern[] star = MethodPattern.ParseAll("*");
            MethodPattern[] widened = new MethodPattern[included.Length + star.Length];
            star.CopyTo(widened, 0);
            included.CopyTo(widened, star.Length);
            included = widened;
        }
        MethodPattern[] excluded = MethodPattern.ParseAll(ignore, negate: true);
        if (excluded.Length == 0)
            return included;

        MethodPattern[] all = new MethodPattern[included.Length + excluded.Length];
        included.CopyTo(all, 0);
        excluded.CopyTo(all, included.Length);
        return all;
    }

    /// <summary>Queues an unpatch for everything this runner applied that the new filters drop.</summary>
    private static void QueueStaleRemovals(MethodPattern[] includes, MethodPattern[] excludes) {
        for (int i = s_Applied.Count - 1; i >= 0; i--) {
            AppliedPatch applied = s_Applied[i];
            if (StillWanted(applied.Target, includes, excludes))
                continue;

            s_Removing.Add(applied.PatchId);
            s_Reverted.Add(applied.Target);
            s_Applied.RemoveAt(i);
        }
    }

    /// <summary>Unpatches everything whose section the judge muted, so even the glue cost goes.</summary>
    private static void QueueMutedRemovals() {
        for (int i = s_Applied.Count - 1; i >= 0; i--) {
            AppliedPatch applied = s_Applied[i];
            if (!SectionRegistry.IsAutoMuted(applied.SectionId))
                continue;

            s_Removing.Add(applied.PatchId);
            s_Reverted.Add(applied.Target);
            s_Applied.RemoveAt(i);
        }
    }

    /// <summary>
    /// Puts back anything an earlier filter change unpatched that now matches again. A method
    /// whose section is still auto-muted stays out until the section is re-enabled.
    /// </summary>
    private static void RestoreReverted(AutoInstrumentPlan plan, MethodPattern[] includes, MethodPattern[] excludes) {
        foreach (MethodInfo method in s_Reverted) {
            if (!StillWanted(method, includes, excludes))
                continue;
            if (SectionCatalog.TryGetSectionId(method, out int sectionId) && SectionRegistry.IsAutoMuted(sectionId))
                continue;

            // the scan already counted it against the catalog entry the unpatch left behind.
            if (plan.SkippedAlreadyInstrumented > 0)
                plan.SkippedAlreadyInstrumented--;
            plan.Targets.Add(method);
        }
    }

    /// <summary>
    /// Trims the live patch set to the cap, newest first. The scanner only caps what one scan
    /// queues, so without this a lowered cap would leave every earlier patch running.
    /// </summary>
    private static void EnforceCap(AutoInstrumentPlan plan) {
        int over = s_Applied.Count + plan.Targets.Count - s_MaxTargets;
        if (over <= 0)
            return;

        int fromPlan = over < plan.Targets.Count ? over : plan.Targets.Count;
        if (fromPlan > 0) {
            plan.Targets.RemoveRange(plan.Targets.Count - fromPlan, fromPlan);
            plan.SkippedOverCap += fromPlan;
            over -= fromPlan;
        }

        for (int i = s_Applied.Count - 1; i >= 0 && over > 0; i--, over--) {
            AppliedPatch applied = s_Applied[i];
            s_Removing.Add(applied.PatchId);
            s_Reverted.Add(applied.Target);
            s_Applied.RemoveAt(i);
            plan.SkippedOverCap++;
        }
    }

    private static bool HasPositive(MethodPattern[] patterns) {
        for (int i = 0; i < patterns.Length; i++) {
            if (!patterns[i].Negated)
                return true;
        }
        return false;
    }

    private static bool StillWanted(MethodInfo method, MethodPattern[] includes, MethodPattern[] excludes) {
        if (includes.Length == 0)
            return false;

        System.Type? declaring = method.DeclaringType;
        string assembly = declaring?.Assembly.GetName().Name ?? string.Empty;
        string type = declaring?.FullName ?? string.Empty;
        return AnyMatch(includes, assembly, type, method.Name)
            && !AnyMatch(excludes, assembly, type, method.Name);
    }

    private static bool AnyMatch(MethodPattern[] patterns, string assembly, string type, string method) {
        for (int i = 0; i < patterns.Length; i++) {
            MethodPattern pattern = patterns[i];
            if (pattern.MatchesAssembly(assembly) && pattern.MatchesType(type) && pattern.MatchesMethod(method))
                return true;
        }
        return false;
    }

    /// <summary>Queues a plan. Counts are replaced, so a second Apply reports the newest scan.</summary>
    public static void Submit(AutoInstrumentPlan plan, string ownerId, bool autoMute) {
        Matched = plan.Matched;
        SkippedTrivial = plan.SkippedTrivial;
        SkippedOther = plan.SkippedAlreadyInstrumented + plan.SkippedBlocklisted
            + plan.SkippedOverCap + plan.SkippedIgnored;
        SkippedOverCap = plan.SkippedOverCap;
        Instrumented = 0;
        Refused = 0;

        s_OwnerId = ownerId;
        // a bumped generation tells the worker to abandon whatever older batch it holds.
        Interlocked.Increment(ref s_Generation);
        s_Pending = plan.Targets.Count == 0 ? null : plan.Targets.ToArray();

        AutoMute.Enabled = autoMute;
        if (s_Pending is not null)
            AutoMute.Arm();
    }

    /// <summary>Called once per rendered frame. Two static loads and a branch when idle.</summary>
    public static void Pump() {
        if (AutoInstrumentRequest.TryTake(out bool enabled, out string filters, out string ignore, out bool autoMute))
            ApplyFilters(enabled ? filters : null, ignore, autoMute, Settings.CollectorRuntimeInfo.OwnerId);

        int muted = AutoMute.MutedCount;
        if (muted != s_LastMutedSeen) {
            s_LastMutedSeen = muted;
            QueueMutedRemovals();
        }

        MethodInfo[]? pending = s_Pending;
        if (pending is not null) {
            s_Pending = null;
            Interlocked.Add(ref s_InFlight, pending.Length);
            lock (s_WorkGate) {
                s_WorkerBatch = new WorkBatch(pending, s_OwnerId, s_Generation);
            }
            EnsureWorker();
            s_WorkArrived.Set();
        }

        DrainWorkerResults();

        if (s_RemoveNext == s_Removing.Count)
            return;

        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BudgetMillis / 1000.0);

        while (s_RemoveNext < s_Removing.Count) {
            PatchRegistry.Remove(s_Removing[s_RemoveNext++]);
            if (Stopwatch.GetTimestamp() >= deadline)
                return;
        }
        s_Removing.Clear();
        s_RemoveNext = 0;
    }

    private static void EnsureWorker() {
        if (s_Worker is not null)
            return;
        s_Worker = new Thread(WorkerLoop) {
            Name = "RimObs.PatchPump",
            IsBackground = true,
        };
        s_Worker.Start();
    }

    private static void WorkerLoop() {
        while (!s_WorkerStop.IsSet) {
            s_WorkArrived.WaitOne();
            WorkBatch? batch;
            lock (s_WorkGate) {
                batch = s_WorkerBatch;
                s_WorkerBatch = null;
            }
            if (batch is null)
                continue;

            MethodInfo[] targets = batch.Targets;
            for (int i = 0; i < targets.Length; i++) {
                if (Volatile.Read(ref s_Generation) != batch.Generation) {
                    Interlocked.Add(ref s_InFlight, -(targets.Length - i));
                    break;
                }
                PatchOneOnWorker(targets[i], batch.OwnerId, batch.Generation);
                Interlocked.Decrement(ref s_InFlight);
            }
        }
    }

    private static void PatchOneOnWorker(MethodInfo method, string ownerId, int generation) {
        try {
            bool wasInCatalog = SectionCatalog.TryGetSectionId(method, out int _);
            ApplyResult result = PatchRegistry.Apply(ownerId, method, MethodResolver.BuildSignature(method));
            s_Results.Enqueue(new WorkResult(
                method, result.PatchId, result.SectionId,
                result.Status == PatchStatus.Active, wasInCatalog, generation));
        }
        catch (System.Exception ex) {
            // one unpatchable target must not stop the run; the reason names the backend bug.
            RimWorks.RimLogging.Log.WarnTo(
                Logging.LogChannels.Sections,
                "auto-instrument refused {Method}: {Reason}",
                new object?[] { $"{method.DeclaringType?.FullName}::{method.Name}", ex.Message });
            s_Results.Enqueue(new WorkResult(method, 0, -1, Active: false, WasInCatalog: false, generation));
        }
    }

    private static void DrainWorkerResults() {
        while (s_Results.TryDequeue(out WorkResult? result)) {
            if (!result.Active) {
                Refused++;
                continue;
            }

            Instrumented++;
            // a catalog entry we did not leave behind means someone else owns this method, so
            // patch it but never revert it.
            bool foreign = result.WasInCatalog && !s_Reverted.Remove(result.Method);
            if (result.Generation != s_Generation
                && !StillWanted(result.Method, s_CurrentIncludes, s_CurrentExcludes)) {
                // landed after the filters moved on: physically patched, so queue the undo.
                s_Removing.Add(result.PatchId);
                s_Reverted.Add(result.Method);
                continue;
            }

            if (!foreign)
                s_Applied.Add(new AppliedPatch(result.Method, result.PatchId, result.SectionId));
            AutoMute.Watch(result.SectionId);
        }
    }

    public static string BuildSummary() {
        if (Matched == 0 && s_Pending is null)
            return "off";

        return $"{Instrumented} instrumented, {Muted} muted, {SkippedTrivial} trivial, "
            + $"{SkippedOther} other, {Refused} refused, {Pending} pending of {Matched} matched";
    }

    internal static void ResetForTests() {
        AutoInstrumentRequest.ResetForTests();
        s_Pending = null;
        // the bump makes a mid-batch worker abandon the rest; then wait for it to let go.
        Interlocked.Increment(ref s_Generation);
        SpinWait.SpinUntil(() => Volatile.Read(ref s_InFlight) == 0, 2000);
        while (s_Results.TryDequeue(out WorkResult? _)) {
            // drop results from the abandoned batch; the next test starts clean.
        }
        s_CurrentIncludes = [];
        s_CurrentExcludes = [];
        s_OwnerId = string.Empty;
        s_Applied.Clear();
        s_Removing.Clear();
        s_RemoveNext = 0;
        s_Reverted.Clear();
        s_LastMutedSeen = 0;
        Matched = 0;
        SkippedOverCap = 0;
        s_MaxTargets = AutoInstrumentScanner.DefaultMaxTargets;
        SkippedTrivial = 0;
        SkippedOther = 0;
        Instrumented = 0;
        Refused = 0;
    }

    private readonly struct AppliedPatch {
        public AppliedPatch(MethodInfo target, int patchId, int sectionId) {
            Target = target;
            PatchId = patchId;
            SectionId = sectionId;
        }

        public MethodInfo Target { get; }

        public int PatchId { get; }

        public int SectionId { get; }
    }
}
