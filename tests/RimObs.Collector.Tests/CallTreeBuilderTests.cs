using System.Collections.Generic;
using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class CallTreeBuilderTests {
    private static CallEdgeStats Edge(int parentId, int sectionId, long callCount, long totalTicks) {
        return new CallEdgeStats {
            ParentId = parentId,
            SectionId = sectionId,
            CallCount = callCount,
            TotalElapsedTicks = totalTicks,
        };
    }

    [Fact]
    public void Build_returns_empty_for_no_edges() {
        CallTreeBuilder.Build([], new Dictionary<int, string>(), 1.0).Should().BeEmpty();
    }

    [Fact]
    public void Build_promotes_no_parent_edges_to_roots() {
        List<CallEdgeStats> edges = [
            Edge(CallTreeBuilder.NoParent, 1, 3, 300),
            Edge(CallTreeBuilder.NoParent, 2, 1, 100),
        ];
        Dictionary<int, string> names = new() { [1] = "a", [2] = "b" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 1.0);

        roots.Should().HaveCount(2);
        roots[0].SectionId.Should().Be(1);
        roots[0].Name.Should().Be("a");
        roots[0].CallCount.Should().Be(3);
        roots[0].TotalNs.Should().Be(300);
        roots[1].SectionId.Should().Be(2);
    }

    [Fact]
    public void Build_nests_children_under_parents() {
        List<CallEdgeStats> edges = [
            Edge(CallTreeBuilder.NoParent, 1, 1, 500),
            Edge(1, 2, 2, 200),
            Edge(2, 3, 4, 50),
        ];
        Dictionary<int, string> names = new() { [1] = "root", [2] = "mid", [3] = "leaf" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 1.0);

        roots.Should().ContainSingle();
        CallTreeNode mid = roots[0].Children.Single();
        mid.SectionId.Should().Be(2);
        mid.Children.Single().SectionId.Should().Be(3);
    }

    [Fact]
    public void Build_orders_siblings_by_total_ticks_descending() {
        List<CallEdgeStats> edges = [
            Edge(CallTreeBuilder.NoParent, 1, 1, 100),
            Edge(1, 2, 1, 10),
            Edge(1, 3, 1, 90),
            Edge(1, 4, 1, 50),
        ];
        Dictionary<int, string> names = new() { [1] = "r", [2] = "low", [3] = "high", [4] = "mid" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 1.0);

        roots[0].Children.Select(c => c.SectionId).Should().ContainInOrder(3, 4, 2);
    }

    [Fact]
    public void Build_stops_descending_at_depth_cap() {
        List<CallEdgeStats> edges = [
            Edge(CallTreeBuilder.NoParent, 1, 1, 10),
            Edge(1, 2, 1, 10),
            Edge(2, 3, 1, 10),
        ];
        Dictionary<int, string> names = new() { [1] = "a", [2] = "b", [3] = "c" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 1.0, depthCap: 2);

        roots[0].Children.Single().SectionId.Should().Be(2);
        roots[0].Children.Single().Children.Should().BeEmpty();
    }

    [Fact]
    public void Build_collapses_beyond_top_n_into_other_node() {
        List<CallEdgeStats> edges = [
            Edge(CallTreeBuilder.NoParent, 1, 1, 100),
            Edge(CallTreeBuilder.NoParent, 2, 1, 80),
            Edge(CallTreeBuilder.NoParent, 3, 5, 30),
            Edge(CallTreeBuilder.NoParent, 4, 7, 20),
        ];
        Dictionary<int, string> names = new() { [1] = "a", [2] = "b", [3] = "c", [4] = "d" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 1.0, topN: 2);

        roots.Should().HaveCount(3);
        CallTreeNode other = roots[^1];
        other.IsOther.Should().BeTrue();
        other.SectionId.Should().Be(CallTreeBuilder.OtherSectionId);
        other.Name.Should().Be("(other)");
        other.CallCount.Should().Be(12);
        other.TotalNs.Should().Be(50);
    }

    [Fact]
    public void Build_guards_against_recursive_cycles() {
        List<CallEdgeStats> edges = [
            Edge(CallTreeBuilder.NoParent, 1, 1, 100),
            Edge(1, 2, 1, 50),
            Edge(2, 1, 1, 25),
        ];
        Dictionary<int, string> names = new() { [1] = "a", [2] = "b" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 1.0);

        CallTreeNode b = roots[0].Children.Single();
        b.SectionId.Should().Be(2);
        CallTreeNode cyclic = b.Children.Single();
        cyclic.SectionId.Should().Be(1);
        cyclic.Children.Should().BeEmpty();
    }

    [Fact]
    public void Build_applies_ns_per_tick_scaling() {
        List<CallEdgeStats> edges = [Edge(CallTreeBuilder.NoParent, 1, 1, 100)];
        Dictionary<int, string> names = new() { [1] = "a" };

        IReadOnlyList<CallTreeNode> roots = CallTreeBuilder.Build(edges, names, 2.5);

        roots[0].TotalNs.Should().Be(250);
    }
}
