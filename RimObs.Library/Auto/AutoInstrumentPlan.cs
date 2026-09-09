using System.Collections.Generic;
using System.Reflection;

namespace RimWorks.RimObs.Auto;

/// <summary>What a filter scan found. Counts cover every match, even past the target cap.</summary>
internal sealed class AutoInstrumentPlan {
    public int Matched { get; set; }

    public int SkippedTrivial { get; set; }

    public int SkippedAlreadyInstrumented { get; set; }

    public int SkippedBlocklisted { get; set; }

    /// <summary>Matched, eligible, but past <c>maxTargets</c>. These are never patched.</summary>
    public int SkippedOverCap { get; set; }

    public List<MethodInfo> Targets { get; } = new List<MethodInfo>();

    public int Eligible => Targets.Count;
}
