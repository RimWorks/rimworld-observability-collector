using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using RimWorks.RimObs.Library.Control;
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

    public static int Matched { get; private set; }

    public static int SkippedTrivial { get; private set; }

    public static int SkippedOther { get; private set; }

    public static int Instrumented { get; private set; }

    public static int Refused { get; private set; }

    public static int Muted => AutoMute.MutedCount;

    public static int Pending => s_Pending is null ? 0 : s_Pending.Length - s_Next;

    /// <summary>Scans and queues in one call. The scan is blocking; the patching is not.</summary>
    public static AutoInstrumentPlan ApplyFilters(
        string? filters, bool autoMute, string ownerId, IEnumerable<Assembly>? assemblies = null
    ) {
        MethodPattern[] patterns = MethodPattern.ParseAll(filters);
        AutoInstrumentPlan plan = AutoInstrumentScanner.Scan(assemblies ?? AssemblyIndex.Enumerate(), patterns);
        Submit(plan, ownerId, autoMute);
        return plan;
    }

    /// <summary>Queues a plan. Counts are replaced, so a second Apply reports the newest scan.</summary>
    public static void Submit(AutoInstrumentPlan plan, string ownerId, bool autoMute) {
        Matched = plan.Matched;
        SkippedTrivial = plan.SkippedTrivial;
        SkippedOther = plan.SkippedAlreadyInstrumented + plan.SkippedBlocklisted + plan.SkippedOverCap;
        Instrumented = 0;
        Refused = 0;

        s_OwnerId = ownerId;
        s_Next = 0;
        s_Pending = plan.Targets.Count == 0 ? null : plan.Targets.ToArray();

        AutoMute.Enabled = autoMute;
        if (s_Pending is not null)
            AutoMute.Arm();
    }

    /// <summary>Called once per rendered frame. One static load and a branch when idle.</summary>
    public static void Pump() {
        MethodInfo[]? pending = s_Pending;
        if (pending is null)
            return;

        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BudgetMillis / 1000.0);
        while (s_Next < pending.Length) {
            PatchOne(pending[s_Next++]);
            if (Stopwatch.GetTimestamp() >= deadline)
                return;
        }

        s_Pending = null;
    }

    private static void PatchOne(MethodInfo method) {
        try {
            ApplyResult result = PatchRegistry.Apply(s_OwnerId, method, MethodResolver.BuildSignature(method));
            if (result.Status != PatchStatus.Active) {
                Refused++;
                return;
            }
            Instrumented++;
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
        s_Pending = null;
        s_Next = 0;
        s_OwnerId = string.Empty;
        Matched = 0;
        SkippedTrivial = 0;
        SkippedOther = 0;
        Instrumented = 0;
        Refused = 0;
    }
}
