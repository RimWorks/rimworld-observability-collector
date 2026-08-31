using System.Collections.Generic;

namespace RimWorks.RimObs.Collector.Aggregation;

public sealed class CallTreeNode {
    public int SectionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long CallCount { get; init; }
    public long TotalNs { get; init; }
    public bool IsOther { get; init; }
    public List<CallTreeNode> Children { get; } = [];
}
