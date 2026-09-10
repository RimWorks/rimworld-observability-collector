using FluentAssertions;
using RimWorks.RimObs.Auto;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Auto;

public class MethodPatternTests {
    [Theory]
    [InlineData("*", "anything", true)]
    [InlineData("Verse.*", "Verse.Map", true)]
    [InlineData("Verse.*", "RimWorld.Map", false)]
    [InlineData("*.Map", "Verse.Map", true)]
    [InlineData("Verse.?ap", "Verse.Map", true)]
    [InlineData("Verse.?ap", "Verse.Maap", false)]
    [InlineData("*Tick*", "Verse.TickManager", true)]
    [InlineData("Verse.Map", "verse.map", true)]
    [InlineData("a*b*c", "axxbyyc", true)]
    [InlineData("a*b*c", "axxbyy", false)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    public void Globs(string pattern, string value, bool expected) {
        MethodPattern.Glob(pattern, value).Should().Be(expected);
    }

    [Fact]
    public void Parses_all_three_parts() {
        MethodPattern p = MethodPattern.Parse("Assembly-CSharp!Verse.Map::MapPreTick")!;

        p.Assembly.Should().Be("Assembly-CSharp");
        p.Type.Should().Be("Verse.Map");
        p.Method.Should().Be("MapPreTick");
    }

    [Fact]
    public void Missing_assembly_and_method_default_to_any() {
        MethodPattern p = MethodPattern.Parse("Verse.*")!;

        p.Assembly.Should().Be("*");
        p.Type.Should().Be("Verse.*");
        p.Method.Should().Be("*");
    }

    [Fact]
    public void Assembly_only_line_matches_every_type_in_it() {
        MethodPattern p = MethodPattern.Parse("Assembly-CSharp!*")!;

        p.MatchesAssembly("Assembly-CSharp").Should().BeTrue();
        p.MatchesType("Verse.Anything").Should().BeTrue();
        p.MatchesMethod("Anything").Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    [InlineData("*")]
    [InlineData("*!*::*")]
    public void Rejects_blank_comment_and_match_everything_lines(string line) {
        MethodPattern.Parse(line).Should().BeNull();
    }

    [Fact]
    public void ParseAll_drops_blanks_and_comments() {
        MethodPattern[] parsed = MethodPattern.ParseAll("Verse.*\n\n# note\nRimWorld.*\n");

        parsed.Should().HaveCount(2);
        parsed[0].Type.Should().Be("Verse.*");
        parsed[1].Type.Should().Be("RimWorld.*");
    }

    [Fact]
    public void ParseAll_of_null_is_empty() {
        MethodPattern.ParseAll(null).Should().BeEmpty();
    }

    [Fact]
    public void Leading_bang_negates_and_the_rest_still_parses() {
        MethodPattern p = MethodPattern.Parse("!Assembly-CSharp!Verse.Log::*")!;

        p.Negated.Should().BeTrue();
        p.Assembly.Should().Be("Assembly-CSharp");
        p.Type.Should().Be("Verse.Log");
        p.Method.Should().Be("*");
    }

    [Fact]
    public void A_plain_line_is_not_negated() {
        MethodPattern.Parse("Verse.*")!.Negated.Should().BeFalse();
    }

    [Fact]
    public void ParseAll_can_force_every_line_negated_for_the_ignore_box() {
        MethodPattern[] parsed = MethodPattern.ParseAll("Verse.Log*\nRimWorld.*", negate: true);

        parsed.Should().HaveCount(2);
        parsed.Should().OnlyContain(p => p.Negated);
    }

    [Fact]
    public void Forcing_negation_does_not_double_negate_an_already_negated_line() {
        MethodPattern[] parsed = MethodPattern.ParseAll("!Verse.Log*", negate: true);

        parsed.Should().ContainSingle().Which.Negated.Should().BeTrue();
    }
}
