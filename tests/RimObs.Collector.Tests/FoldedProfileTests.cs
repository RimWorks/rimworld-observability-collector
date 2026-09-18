using System.Collections.Generic;
using FluentAssertions;
using RimWorks.RimObs.Collector.Push;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class FoldedProfileTests {
    // one tick is one microsecond, so a tick count reads straight through as micros.
    private const double NsPerTick = 1000;

    private static readonly Dictionary<int, string> Names = new() {
        [1] = "Root",
        [2] = "Pawns",
        [3] = "Pathing",
        [4] = "Weather",
    };

    private static string[] Lines(string folded) =>
        folded.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void A_root_with_no_children_is_all_self_time() {
        string folded = FoldedProfile.Encode(
            [new ProfileEdge(FoldedProfile.NoParent, 1, 500)], Names, NsPerTick);

        Lines(folded).Should().Equal("Root 500");
    }

    [Fact]
    public void A_child_takes_its_time_out_of_the_parents_self_line() {
        string folded = FoldedProfile.Encode([
            new ProfileEdge(FoldedProfile.NoParent, 1, 1000),
            new ProfileEdge(1, 2, 400),
        ], Names, NsPerTick);

        Lines(folded).Should().BeEquivalentTo("Root 600", "Root;Pawns 400");
    }

    [Fact]
    public void A_parent_that_only_delegates_emits_no_self_line() {
        string folded = FoldedProfile.Encode([
            new ProfileEdge(FoldedProfile.NoParent, 1, 300),
            new ProfileEdge(1, 2, 300),
        ], Names, NsPerTick);

        Lines(folded).Should().Equal("Root;Pawns 300");
    }

    // children hang off a section rather than off one caller, so two callers of Pawns have to
    // split Pathing's time or the total comes out larger than the session.
    [Fact]
    public void A_section_called_from_two_parents_splits_its_childrens_time() {
        string folded = FoldedProfile.Encode([
            new ProfileEdge(FoldedProfile.NoParent, 1, 1000),
            new ProfileEdge(FoldedProfile.NoParent, 4, 1000),
            new ProfileEdge(1, 2, 750),
            new ProfileEdge(4, 2, 250),
            new ProfileEdge(2, 3, 400),
        ], Names, NsPerTick);

        // Pawns holds 1000 ticks total, so the 750 edge absorbs 3/4 of Pathing's 400.
        Lines(folded).Should().Contain("Root;Pawns 450").And.Contain("Weather;Pawns 150");
    }

    [Fact]
    public void A_cycle_stops_instead_of_recursing_forever() {
        string folded = FoldedProfile.Encode([
            new ProfileEdge(FoldedProfile.NoParent, 1, 900),
            new ProfileEdge(1, 2, 600),
            new ProfileEdge(2, 1, 300),
        ], Names, NsPerTick);

        folded.Should().NotBeEmpty();
        Lines(folded).Should().OnlyContain(l => l.Split(';').Length <= FoldedProfile.MaxDepth);
    }

    [Fact]
    public void A_name_with_a_separator_in_it_stays_one_frame() {
        Dictionary<int, string> names = new() { [1] = "Mod;With Spaces" };

        string folded = FoldedProfile.Encode(
            [new ProfileEdge(FoldedProfile.NoParent, 1, 100)], names, NsPerTick);

        Lines(folded).Should().Equal("Mod:With_Spaces 100");
    }

    [Fact]
    public void An_unnamed_section_falls_back_to_its_id() {
        string folded = FoldedProfile.Encode(
            [new ProfileEdge(FoldedProfile.NoParent, 99, 100)], Names, NsPerTick);

        Lines(folded).Should().Equal("section_99 100");
    }

    [Fact]
    public void An_edge_that_did_not_move_this_window_is_dropped() {
        string folded = FoldedProfile.Encode([
            new ProfileEdge(FoldedProfile.NoParent, 1, 500),
            new ProfileEdge(1, 2, 0),
        ], Names, NsPerTick);

        Lines(folded).Should().Equal("Root 500");
    }

    [Fact]
    public void Nothing_to_say_encodes_to_nothing() {
        FoldedProfile.Encode([], Names, NsPerTick).Should().BeEmpty();
        FoldedProfile.Encode([new ProfileEdge(1, 2, 50)], Names, NsPerTick)
            .Should().BeEmpty("a tree with no root has nothing to hang a stack on");
    }
}
