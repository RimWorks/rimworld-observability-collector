using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Profile;

/// <summary>
/// Drops auto-instrumented sections whose mean self time never exceeds the cost of measuring
/// them. Only sections handed to <see cref="Watch"/> are ever considered.
/// </summary>
internal static class AutoMute {
    /// <summary>Enabled Start/Stop pair, measured at 44-45 ns in game and 67-74 ns offline.</summary>
    public const long ScopeOverheadNanos = 75;

    /// <summary>Calls observed before a section is judged once and never re-judged.</summary>
    public const int SampleCount = 256;

    /// <summary>Set once when auto-instrumentation submits its first batch. Never cleared.</summary>
    public static volatile bool Armed;

    /// <summary>Gates the mute decision only. Counting runs whenever <see cref="Armed"/> is set.</summary>
    public static volatile bool Enabled = true;

    private static int[]? s_Calls;
    private static long[]? s_SelfTicks;
    private static bool[]? s_Watched;
    private static int s_Muted;
    private static int s_Judged;
    private static long s_Budget;

    public static int MutedCount => s_Muted;

    public static int JudgedCount => s_Judged;

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
        s_Watched = new bool[SectionRegistry.MaxSections];
        s_Budget = Stopwatch.Frequency * ScopeOverheadNanos * SampleCount / 1_000_000_000L;
        Armed = true;
    }

    public static void Watch(int sectionId) {
        bool[]? watched = s_Watched;
        if (watched is null || (uint)sectionId >= (uint)watched.Length)
            return;
        watched[sectionId] = true;
        s_Calls![sectionId] = 0;
        s_SelfTicks![sectionId] = 0;
    }

    /// <summary>Hot path. Counts one call and, at the sample size, judges the section once.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Observe(int sectionId, long selfTicks) {
        bool[]? watched = s_Watched;
        if (watched is null || (uint)sectionId >= (uint)watched.Length || !watched[sectionId])
            return;

        int n = ++s_Calls![sectionId];
        s_SelfTicks![sectionId] += selfTicks;
        if (n < SampleCount)
            return;

        watched[sectionId] = false;
        s_Judged++;
        if (!Enabled)
            return;

        if (s_SelfTicks[sectionId] <= s_Budget) {
            SectionRegistry.SetActive(sectionId, false);
            s_Muted++;
        }
    }

    internal static void ResetForTests() {
        Armed = false;
        Enabled = true;
        s_Calls = null;
        s_SelfTicks = null;
        s_Watched = null;
        s_Muted = 0;
        s_Judged = 0;
        s_Budget = 0;
    }
}
