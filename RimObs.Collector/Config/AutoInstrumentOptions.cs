namespace RimWorks.RimObs.Collector.Config;

public sealed class AutoInstrumentOptions {
    public const int DefaultMaxTargets = 8192;
    public const int MinMaxTargets = 1;
    public const int MaxMaxTargets = 1000000;

    public bool Enabled { get; set; }

    /// <summary>Log spam and property accessors: the flood is mostly sub-inline getters.</summary>
    public const string DefaultIgnore = "Assembly-CSharp!Verse.Log::*\n*::get_*\n*::set_*\n*::GetHashCode\n*::Equals\n*::ToString";

    // one Assembly!Type::Method glob per line. a leading ! on a line excludes.
    public string Filters { get; set; } = string.Empty;

    public string Ignore { get; set; } = DefaultIgnore;

    public bool MuteTrivial { get; set; } = true;

    // ceiling on how many methods one scan may plan. past it the plan truncates silently.
    public int MaxTargets { get; set; } = DefaultMaxTargets;

    // per-frame scope-overhead budget in microseconds; the library mutes chatter to fit it.
    public long OverheadBudgetUs { get; set; } = 1000;

    public static int ClampMaxTargets(int value) {
        int floored = value < MinMaxTargets ? MinMaxTargets : value;
        return floored > MaxMaxTargets ? MaxMaxTargets : floored;
    }
}
