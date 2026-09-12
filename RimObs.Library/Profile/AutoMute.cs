using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RimWorks.RimObs.Profile;

/// <summary>
/// Drops auto-instrumented sections whose mean self time never exceeds the cost of measuring
/// them. Only sections handed to <see cref="Watch"/> are ever considered.
/// </summary>
internal static class AutoMute {
    /// <summary>Enabled Start/Stop pair, measured at 44-45 ns in game and 67-74 ns offline.</summary>
    public const long ScopeOverheadNanos = 75;

    /// <summary>Calls observed before a section is judged once and never re-judged. 64 keeps
    /// the verdict honest while letting chatter die within a second of being patched.</summary>
    public const int SampleCount = 64;

    /// <summary>Set once when auto-instrumentation submits its first batch. Never cleared.</summary>
    public static volatile bool Armed;

    /// <summary>Gates the mute decision only. Counting runs whenever <see cref="Armed"/> is set.</summary>
    public static volatile bool Enabled = true;

    private static int[]? s_Calls;
    private static long[]? s_SelfTicks;
    // auto-instrumented and so fair game for muting; survives the one-shot judgment.
    private static bool[]? s_Auto;
    // still awaiting the one-shot below-measurement-cost judgment.
    private static bool[]? s_Pending;
    private static int[]? s_CandidateIds;
    private static long[]? s_CandidateKeys;
    private static int s_Muted;
    private static int s_Judged;
    private static long s_Budget;

    public static int MutedCount => s_Muted;

    public static int JudgedCount => s_Judged;

    /// <summary>Forced verdicts below this many calls are noise; the section just unhides.</summary>
    public const int ForcedJudgeCallFloor = 8;

    /// <summary>How often the budget judge runs, in frames. ~1s at 60fps, so the tick tax of
    /// freshly patched chatter is clipped fast during the instrumentation ramp.</summary>
    public const int JudgeWindowFrames = 60;

    private static long s_BudgetUsPerFrame = 1000;

    /// <summary>Per-frame scope-overhead budget in microseconds. 0 disables the budget judge.</summary>
    public static long BudgetUsPerFrame {
        get => Interlocked.Read(ref s_BudgetUsPerFrame);
        set => Interlocked.Exchange(ref s_BudgetUsPerFrame, value);
    }

    private static int s_LastJudgeOrdinal;

    /// <summary>Runs the budget judge once per window. Called off the main thread by the sender.</summary>
    public static void JudgeIfDue(int frameOrdinal) {
        int last = s_LastJudgeOrdinal;
        if (frameOrdinal - last < JudgeWindowFrames)
            return;
        s_LastJudgeOrdinal = frameOrdinal;
        if (last != 0)
            JudgeBudget(frameOrdinal - last);
    }

    /// <summary>
    /// Mutes the cheapest chatty sections until the projected per-frame scope tax fits the
    /// budget, then starts a fresh counting window. Expensive sections keep measuring.
    /// </summary>
    public static void JudgeBudget(int framesElapsed) {
        int[]? calls = s_Calls;
        long[]? self = s_SelfTicks;
        bool[]? auto = s_Auto;
        if (calls is null || self is null || auto is null)
            return;
        if (!Enabled || framesElapsed <= 0 || BudgetUsPerFrame <= 0) {
            ResetWindow(calls, self);
            return;
        }

        long perCallTicks = Math.Max(1L, Stopwatch.Frequency * ScopeOverheadNanos / 1_000_000_000L);
        long budgetTicks = Stopwatch.Frequency * BudgetUsPerFrame * framesElapsed / 1_000_000L;
        // force-judge stragglers short of the one-shot sample size. below the call floor the
        // evidence is noise, so pending clears and the one-shot decides at full size later.
        bool[]? pendingFlags = s_Pending;
        if (pendingFlags is not null) {
            for (int id = 0; id < calls.Length; id++) {
                if (!pendingFlags[id] || calls[id] == 0)
                    continue;
                if (calls[id] < ForcedJudgeCallFloor) {
                    pendingFlags[id] = false;
                    continue;
                }
                pendingFlags[id] = false;
                s_Judged++;
                if (Enabled && self[id] <= s_Budget * calls[id] / SampleCount) {
                    SectionRegistry.MuteAuto(id);
                    s_Muted++;
                }
            }
        }

        long totalTicks = 0;
        // preallocated at Arm: this runs every window on the sender thread, and a fresh list
        // plus a closure sort was a once-a-second Boehm collection trigger.
        int[] candidates = s_CandidateIds!;
        long[] keys = s_CandidateKeys!;
        int candidateCount = 0;
        for (int id = 0; id < calls.Length; id++) {
            if (calls[id] == 0 || !SectionRegistry.IsActive(id))
                continue;
            totalTicks += calls[id] * perCallTicks;
            if (auto[id]) {
                candidates[candidateCount] = id;
                keys[candidateCount] = self[id] / calls[id];
                candidateCount++;
            }
        }

        if (totalTicks > budgetTicks) {
            // cheapest mean self time first: those measure the least work per unit of tax.
            Array.Sort(keys, candidates, 0, candidateCount);
            for (int i = 0; i < candidateCount; i++) {
                if (totalTicks <= budgetTicks)
                    break;
                int id = candidates[i];
                SectionRegistry.MuteAuto(id);
                s_Muted++;
                totalTicks -= calls[id] * perCallTicks;
            }
        }

        ResetWindow(calls, self);
    }

    private static void ResetWindow(int[] calls, long[] self) {
        Array.Clear(calls, 0, calls.Length);
        Array.Clear(self, 0, self.Length);
    }

    /// <summary>Sections awaiting judgment send nothing: the judge reads its own accumulators,
    /// so pre-judgment samples were pure flood. Hot path: one array read.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPending(int sectionId) {
        // muting off means judgment never comes, so nothing may be held back.
        if (!Enabled)
            return false;
        bool[]? pending = s_Pending;
        return pending is not null && (uint)sectionId < (uint)pending.Length && pending[sectionId];
    }

    /// <summary>Self ticks accumulated so far. The fold in Profiler.StopById is not observable otherwise.</summary>
    internal static long SelfTicksOf(int sectionId) =>
        s_SelfTicks is null || (uint)sectionId >= (uint)s_SelfTicks.Length ? 0 : s_SelfTicks[sectionId];

    /// <summary>Total self ticks a section may spend across <see cref="SampleCount"/> calls.</summary>
    public static long BudgetTicks => s_Budget;

    public static void Arm() {
        if (s_Calls is not null) {
            Armed = true;
            return;
        }

        s_Calls = new int[SectionRegistry.MaxSections];
        s_SelfTicks = new long[SectionRegistry.MaxSections];
        s_Auto = new bool[SectionRegistry.MaxSections];
        s_Pending = new bool[SectionRegistry.MaxSections];
        s_CandidateIds = new int[SectionRegistry.MaxSections];
        s_CandidateKeys = new long[SectionRegistry.MaxSections];
        s_Budget = Stopwatch.Frequency * ScopeOverheadNanos * SampleCount / 1_000_000_000L;
        Armed = true;
    }

    public static void Watch(int sectionId) {
        bool[]? auto = s_Auto;
        if (auto is null || (uint)sectionId >= (uint)auto.Length)
            return;
        auto[sectionId] = true;
        s_Pending![sectionId] = true;
        s_Calls![sectionId] = 0;
        s_SelfTicks![sectionId] = 0;
    }

    /// <summary>Hot path. Counts one call and, at the sample size, judges the section once.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Observe(int sectionId, long selfTicks) {
        bool[]? auto = s_Auto;
        if (auto is null || (uint)sectionId >= (uint)auto.Length || !auto[sectionId])
            return;

        int n = ++s_Calls![sectionId];
        s_SelfTicks![sectionId] += selfTicks;
        if (n != SampleCount || !s_Pending![sectionId])
            return;

        s_Pending[sectionId] = false;
        s_Judged++;
        if (!Enabled)
            return;

        if (s_SelfTicks[sectionId] <= s_Budget) {
            SectionRegistry.MuteAuto(sectionId);
            s_Muted++;
        }
    }

    internal static void ResetForTests() {
        Armed = false;
        Enabled = true;
        s_Calls = null;
        s_SelfTicks = null;
        s_Auto = null;
        s_Pending = null;
        s_CandidateIds = null;
        s_CandidateKeys = null;
        s_Muted = 0;
        s_Judged = 0;
        s_Budget = 0;
        s_LastJudgeOrdinal = 0;
        s_BudgetUsPerFrame = 1000;
    }
}
