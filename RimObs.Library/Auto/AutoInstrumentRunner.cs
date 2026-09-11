using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
    private static int s_Next;
    private static string s_OwnerId = string.Empty;
    private static int s_MaxTargets = AutoInstrumentScanner.DefaultMaxTargets;

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
        (s_Pending is null ? 0 : s_Pending.Length - s_Next) + (s_Removing.Count - s_RemoveNext);

    /// <summary>Scans and queues in one call. The scan is blocking; the patching is not.</summary>
    public static AutoInstrumentPlan ApplyFilters(
        string? filters, string? ignore, bool autoMute, string ownerId, IEnumerable<Assembly>? assemblies = null
    ) {
        MethodPattern[] patterns = Combine(filters, ignore);
        MethodPattern.Split(patterns, out MethodPattern[] includes, out MethodPattern[] excludes);

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
        s_Next = 0;
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
        if (pending is null && s_RemoveNext == s_Removing.Count)
            return;

        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BudgetMillis / 1000.0);

        while (s_RemoveNext < s_Removing.Count) {
            PatchRegistry.Remove(s_Removing[s_RemoveNext++]);
            if (Stopwatch.GetTimestamp() >= deadline)
                return;
        }
        s_Removing.Clear();
        s_RemoveNext = 0;

        if (pending is null)
            return;

        while (s_Next < pending.Length) {
            PatchOne(pending[s_Next++]);
            if (Stopwatch.GetTimestamp() >= deadline)
                return;
        }

        s_Pending = null;
    }

    private static void PatchOne(MethodInfo method) {
        try {
            // a catalog entry we did not leave behind means someone else owns this method, so
            // patch it but never revert it.
            bool foreign = SectionCatalog.TryGetSectionId(method, out int _) && !s_Reverted.Remove(method);
            ApplyResult result = PatchRegistry.Apply(s_OwnerId, method, MethodResolver.BuildSignature(method));
            if (result.Status != PatchStatus.Active) {
                Refused++;
                return;
            }
            Instrumented++;
            if (!foreign)
                s_Applied.Add(new AppliedPatch(method, result.PatchId, result.SectionId));
            AutoMute.Watch(result.SectionId);
        }
        catch (System.Exception) {
            // one unpatchable target must not stop the run. the count is the user-facing signal.
            Refused++;
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
        s_Next = 0;
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
