using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RimWorks.RimObs.Collector.Push;

/// <summary>One call edge's share of a single push window, already differenced against the
/// previous push. Ticks is inclusive: the child plus everything it called.</summary>
public readonly record struct ProfileEdge(int ParentId, int SectionId, long Ticks);

/// <summary>Renders the call tree as pyroscope's folded text. One line per stack: frame names
/// joined by ';', a space, then the microseconds spent in that frame itself.</summary>
public static class FoldedProfile {
    public const int NoParent = -1;

    /// <summary>A cycle the path guard misses still cannot produce an unbounded line.</summary>
    public const int MaxDepth = 64;

    /// <summary>At one million, one sample is one microsecond, so counts read back as wall time.</summary>
    public const int SampleRateHz = 1_000_000;

    public static string Encode(
        IReadOnlyCollection<ProfileEdge> edges,
        IReadOnlyDictionary<int, string> sectionNames,
        double nsPerTick) {
        if (edges is null || edges.Count == 0 || nsPerTick <= 0)
            return string.Empty;

        Dictionary<int, List<ProfileEdge>> childrenByParent = [];
        Dictionary<int, long> inclusiveBySection = [];
        foreach (ProfileEdge edge in edges) {
            if (edge.Ticks <= 0)
                continue;

            if (!childrenByParent.TryGetValue(edge.ParentId, out List<ProfileEdge>? list)) {
                list = [];
                childrenByParent[edge.ParentId] = list;
            }
            list.Add(edge);
            inclusiveBySection.TryGetValue(edge.SectionId, out long soFar);
            inclusiveBySection[edge.SectionId] = soFar + edge.Ticks;
        }

        if (!childrenByParent.TryGetValue(NoParent, out List<ProfileEdge>? roots))
            return string.Empty;

        StringBuilder output = new();
        StringBuilder stack = new();
        Walk(roots, childrenByParent, inclusiveBySection, sectionNames, nsPerTick, output, stack, depth: 0, path: []);
        return output.ToString();
    }

    private static void Walk(
        List<ProfileEdge> level,
        Dictionary<int, List<ProfileEdge>> childrenByParent,
        Dictionary<int, long> inclusiveBySection,
        IReadOnlyDictionary<int, string> sectionNames,
        double nsPerTick,
        StringBuilder output,
        StringBuilder stack,
        int depth,
        HashSet<int> path) {
        if (depth >= MaxDepth)
            return;

        foreach (ProfileEdge edge in level) {
            if (!path.Add(edge.SectionId))
                continue;

            int mark = stack.Length;
            if (mark > 0)
                stack.Append(';');
            stack.Append(Frame(edge.SectionId, sectionNames));

            childrenByParent.TryGetValue(edge.SectionId, out List<ProfileEdge>? children);
            long micros = SelfMicros(edge, children, inclusiveBySection, nsPerTick);
            if (micros > 0) {
                output.Append(stack);
                output.Append(' ');
                output.Append(micros.ToString(CultureInfo.InvariantCulture));
                output.Append('\n');
            }

            if (children is not null)
                Walk(children, childrenByParent, inclusiveBySection, sectionNames, nsPerTick, output, stack, depth + 1, path);

            stack.Length = mark;
            path.Remove(edge.SectionId);
        }
    }

    /// <summary>Children hang off a section, not off one caller of it, so a section reached from
    /// two parents splits its children's time by each parent's share.</summary>
    private static long SelfMicros(
        ProfileEdge edge,
        List<ProfileEdge>? children,
        Dictionary<int, long> inclusiveBySection,
        double nsPerTick) {
        long childTicks = 0;
        if (children is not null) {
            foreach (ProfileEdge child in children)
                childTicks += child.Ticks;
        }

        if (childTicks > 0
            && inclusiveBySection.TryGetValue(edge.SectionId, out long sectionTotal)
            && sectionTotal > 0) {
            childTicks = (long)(childTicks * ((double)edge.Ticks / sectionTotal));
        }

        long selfTicks = edge.Ticks - childTicks;
        if (selfTicks <= 0)
            return 0;

        return (long)(selfTicks * nsPerTick / 1000.0);
    }

    /// <summary>A ';', a space or a newline in a name would split one frame into two.</summary>
    private static string Frame(int sectionId, IReadOnlyDictionary<int, string> sectionNames) {
        if (!sectionNames.TryGetValue(sectionId, out string? name) || string.IsNullOrEmpty(name))
            return "section_" + sectionId.ToString(CultureInfo.InvariantCulture);

        return name.IndexOfAny([';', '\n', '\r', ' ']) < 0
            ? name
            : name.Replace(';', ':').Replace('\n', '_').Replace('\r', '_').Replace(' ', '_');
    }
}
