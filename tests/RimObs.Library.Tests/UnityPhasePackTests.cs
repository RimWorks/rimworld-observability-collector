using System.Collections.Generic;
using System.Linq;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public class UnityPhasePackTests {
    [Fact]
    public void DerivesDottedNamesFromTheTreePath() {
        PhaseNode root = Tree(("PostLateUpdate", new[] { "PresentAfterDraw", "UpdateAllRenderers" }));

        PhasePlanner.TryPlan(root, out PlannedNode? planned).Should().BeTrue();

        Names(planned!).Should().Equal(
            "Unity.PostLateUpdate",
            "Unity.PostLateUpdate.PresentAfterDraw",
            "Unity.PostLateUpdate.UpdateAllRenderers"
        );
    }

    [Fact]
    public void NamingIsStableAcrossRepeatedPlans() {
        PhaseNode first = Tree(("TimeUpdate", new[] { "WaitForLastPresentationAndUpdateTime" }));
        PhaseNode second = Tree(("TimeUpdate", new[] { "WaitForLastPresentationAndUpdateTime" }));

        PhasePlanner.TryPlan(first, out PlannedNode? a);
        PhasePlanner.TryPlan(second, out PlannedNode? b);

        Names(a!).Should().Equal(Names(b!));
    }

    [Fact]
    public void SuffixesDuplicateNamesSoTwoSystemsNeverShareASection() {
        PhaseNode root = Tree(
            ("Update", new[] { "ScriptRunBehaviourUpdate" }),
            ("Update", new[] { "ScriptRunBehaviourUpdate" })
        );

        PhasePlanner.TryPlan(root, out PlannedNode? planned).Should().BeTrue();

        List<string> names = Names(planned!);
        names.Should().OnlyHaveUniqueItems();
        names.Should().Contain("Unity.Update#2");
    }

    [Fact]
    public void WrapsEachSubsystemWithABeginBeforeAndAnEndAfter() {
        PhaseNode root = Tree(("PreUpdate", new[] { "PhysicsUpdate" }));

        PhasePlanner.TryPlan(root, out PlannedNode? planned).Should().BeTrue();

        planned!.Children.Select(c => c.Kind).Should().Equal(
            PlannedKind.Begin, PlannedKind.Original, PlannedKind.End
        );

        PlannedNode phase = planned.Children[1];
        phase.Children.Select(c => c.Kind).Should().Equal(
            PlannedKind.Begin, PlannedKind.Original, PlannedKind.End
        );
        phase.Children[0].SectionName.Should().Be("Unity.PreUpdate.PhysicsUpdate");
        phase.Children[2].SectionName.Should().Be("Unity.PreUpdate.PhysicsUpdate");
    }

    [Fact]
    public void CountsOneMarkerPairPerSubsystem() {
        PhaseNode root = Tree(("PostLateUpdate", new[] { "PresentAfterDraw", "UpdateAllRenderers" }));

        PhasePlanner.TryPlan(root, out _).Should().BeTrue();

        PhasePlanner.LastMarkerCount.Should().Be(3);
    }

    [Fact]
    public void BailsOnAnEmptyTree() {
        PhasePlanner.TryPlan(new PhaseNode(string.Empty, null), out PlannedNode? planned).Should().BeFalse();
        planned.Should().BeNull();
    }

    [Fact]
    public void BailsWhenASystemHasNoTypeName() {
        PhaseNode root = new(string.Empty, null);
        root.Children.Add(new PhaseNode(string.Empty, null));

        PhasePlanner.TryPlan(root, out PlannedNode? planned).Should().BeFalse();
        planned.Should().BeNull();
    }

    [Fact]
    public void BailsWhenTheTreeIsBiggerThanAnyRealPlayerLoop() {
        PhaseNode root = new(string.Empty, null);
        for (int i = 0; i <= UnityPhasePack.MaxNodes; i++)
            root.Children.Add(new PhaseNode("Phase" + i, null));

        PhasePlanner.TryPlan(root, out PlannedNode? planned).Should().BeFalse();
        planned.Should().BeNull();
    }

    [Fact]
    public void BailsWhenTheTreeIsDeeperThanAnyRealPlayerLoop() {
        PhaseNode root = new(string.Empty, null);
        PhaseNode cursor = root;
        for (int i = 0; i <= UnityPhasePack.MaxDepth + 1; i++) {
            PhaseNode child = new("Level" + i, null);
            cursor.Children.Add(child);
            cursor = child;
        }

        PhasePlanner.TryPlan(root, out PlannedNode? planned).Should().BeFalse();
        planned.Should().BeNull();
    }

    [Theory]
    [InlineData("WaitForLastPresentationAndUpdateTime", "idle")]
    [InlineData("PresentAfterDraw", "idle")]
    [InlineData("PresentBeforeUpdate", "idle")]
    [InlineData("TimeUpdate", "idle")]
    [InlineData("UpdateAllRenderers", "engine")]
    [InlineData("PlayerUpdateCanvases", "engine")]
    public void ClassifiesWaitingApartFromWorking(string leaf, string expected) =>
        UnityPhasePack.SubsystemFor(leaf).Should().Be(expected);

    [Fact]
    public void MarkerEmitsOneSamplePerBeginEndPair() {
        RecordingSink sink = new();
        Profiler.SetSink(sink);
        Profiler.SetEnabled(true);
        try {
            PhaseMarker marker = new(SectionRegistry.Register("Unity.Test.Pair", "engine"));
            marker.Begin();
            marker.End();
            marker.End();

            sink.Samples.Should().ContainSingle().Which.SectionId.Should().Be(marker.Handle.Id);
        }
        finally {
            Profiler.SetSink(null);
        }
    }

    [Fact]
    public void MarkerClosesAnOpenSpanBeforeStartingASecondOne() {
        RecordingSink sink = new();
        Profiler.SetSink(sink);
        Profiler.SetEnabled(true);
        try {
            PhaseMarker marker = new(SectionRegistry.Register("Unity.Test.Reentrant", "engine"));
            marker.Begin();
            marker.Begin();
            marker.End();

            sink.Samples.Should().HaveCount(2);
        }
        finally {
            Profiler.SetSink(null);
        }
    }

    [Fact]
    public void InstallAndUninstallNoOpWhenUnityIsAbsent() {
        UnityLoopBinding.Resolve().Should().BeNull();

        UnityPhasePack.InstallAll();
        UnityPhasePack.Uninstall();

        UnityPhasePack.Installed.Should().BeFalse();
        UnityPhasePack.InstalledCount.Should().Be(0);
    }

    private static PhaseNode Tree(params (string Phase, string[] Leaves)[] phases) {
        PhaseNode root = new(string.Empty, null);
        foreach ((string phase, string[] leaves) in phases) {
            PhaseNode node = new(phase, null);
            foreach (string leaf in leaves)
                node.Children.Add(new PhaseNode(leaf, null));
            root.Children.Add(node);
        }
        return root;
    }

    private static List<string> Names(PlannedNode planned) {
        List<string> names = new();
        Collect(planned, names);
        return names;
    }

    private static void Collect(PlannedNode node, List<string> names) {
        foreach (PlannedNode child in node.Children) {
            if (child.Kind != PlannedKind.Original)
                continue;
            names.Add(child.SectionName);
            Collect(child, names);
        }
    }
}
