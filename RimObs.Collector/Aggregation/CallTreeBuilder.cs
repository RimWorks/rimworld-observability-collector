using System.Collections.Generic;
using System.Linq;

namespace RimWorks.RimObs.Collector.Aggregation;

public static class CallTreeBuilder {
    public const int DefaultDepthCap = 10;
    public const int DefaultTopN = 16;
    public const int OtherSectionId = -2;
    public const int NoParent = -1;

    public static IReadOnlyList<CallTreeNode> Build(
        IReadOnlyCollection<CallEdgeStats> edges,
        IReadOnlyDictionary<int, string> sectionNames,
        double nsPerTick,
        int depthCap = DefaultDepthCap,
        int topN = DefaultTopN) {
        if (edges is null || edges.Count == 0)
            return [];

        Dictionary<int, List<CallEdgeStats>> childrenByParent = new();
        foreach (CallEdgeStats edge in edges) {
            if (!childrenByParent.TryGetValue(edge.ParentId, out List<CallEdgeStats>? list)) {
                list = [];
                childrenByParent[edge.ParentId] = list;
            }
            list.Add(edge);
        }

        return BuildLevel(
            childrenByParent.TryGetValue(NoParent, out List<CallEdgeStats>? roots) ? roots : [],
            childrenByParent,
            sectionNames,
            nsPerTick,
            depthCap,
            topN,
            depth: 0,
            path: new HashSet<int>());
    }

    private static List<CallTreeNode> BuildLevel(
        List<CallEdgeStats> levelEdges,
        Dictionary<int, List<CallEdgeStats>> childrenByParent,
        IReadOnlyDictionary<int, string> sectionNames,
        double nsPerTick,
        int depthCap,
        int topN,
        int depth,
        HashSet<int> path) {
        List<CallTreeNode> result = [];
        if (levelEdges.Count == 0)
            return result;

        List<CallEdgeStats> ordered = levelEdges
            .OrderByDescending(e => e.TotalElapsedTicks)
            .ThenBy(e => e.SectionId)
            .ToList();

        int kept = ordered.Count <= topN ? ordered.Count : topN;
        for (int i = 0; i < kept; i++) {
            CallEdgeStats edge = ordered[i];
            CallTreeNode node = new() {
                SectionId = edge.SectionId,
                Name = sectionNames.TryGetValue(edge.SectionId, out string? name) ? name : string.Empty,
                CallCount = edge.CallCount,
                TotalNs = (long)(edge.TotalElapsedTicks * nsPerTick),
            };

            bool canDescend = depth + 1 < depthCap && !path.Contains(edge.SectionId);
            if (canDescend && childrenByParent.TryGetValue(edge.SectionId, out List<CallEdgeStats>? grandchildren)) {
                path.Add(edge.SectionId);
                node.Children.AddRange(BuildLevel(
                    grandchildren, childrenByParent, sectionNames, nsPerTick, depthCap, topN, depth + 1, path));
                path.Remove(edge.SectionId);
            }

            result.Add(node);
        }

        if (ordered.Count > topN) {
            long otherCalls = 0;
            long otherTicks = 0;
            for (int i = topN; i < ordered.Count; i++) {
                otherCalls += ordered[i].CallCount;
                otherTicks += ordered[i].TotalElapsedTicks;
            }
            result.Add(new CallTreeNode {
                SectionId = OtherSectionId,
                Name = "(other)",
                CallCount = otherCalls,
                TotalNs = (long)(otherTicks * nsPerTick),
                IsOther = true,
            });
        }

        return result;
    }
}
