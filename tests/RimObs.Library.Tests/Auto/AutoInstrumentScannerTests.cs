using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using RimObsTest.AutoFixtures;
using RimWorks.RimObs.Auto;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Auto;

public class AutoInstrumentScannerTests {
    private static readonly Assembly[] s_Here = [typeof(AutoTargets).Assembly];

    private static AutoInstrumentPlan Scan(string filters, int maxTargets = AutoInstrumentScanner.DefaultMaxTargets) =>
        AutoInstrumentScanner.Scan(s_Here, MethodPattern.ParseAll(filters), maxTargets);

    [Fact]
    public void Empty_filter_matches_nothing() {
        AutoInstrumentPlan plan = AutoInstrumentScanner.Scan(s_Here, [], 100);

        plan.Matched.Should().Be(0);
        plan.Targets.Should().BeEmpty();
    }

    [Fact]
    public void Matches_every_method_on_a_type() {
        AutoInstrumentPlan plan = Scan("RimObsTest.AutoFixtures.AutoTargets");

        plan.Matched.Should().Be(4);
    }

    [Fact]
    public void Rejects_the_trivial_method_and_keeps_the_rest() {
        AutoInstrumentPlan plan = Scan("RimObsTest.AutoFixtures.AutoTargets");

        plan.SkippedTrivial.Should().Be(1);
        plan.Targets.Select(m => m.Name).Should().BeEquivalentTo(
            "Worthwhile", "AlsoWorthwhile", "ThirdWorthwhile");
    }

    [Fact]
    public void Method_part_narrows_the_match() {
        AutoInstrumentPlan plan = Scan("RimObsTest.AutoFixtures.*::*so*");

        plan.Targets.Select(m => m.Name).Should().BeEquivalentTo("AlsoWorthwhile");
    }

    [Fact]
    public void Wrong_assembly_matches_nothing() {
        AutoInstrumentPlan plan = Scan("NotThisAssembly!RimObsTest.AutoFixtures.*");

        plan.Matched.Should().Be(0);
    }

    [Fact]
    public void Namespace_wildcard_reaches_both_fixture_types() {
        AutoInstrumentPlan plan = Scan("RimObsTest.AutoFixtures.*");

        plan.Targets.Select(m => m.Name).Should().Contain("Elsewhere");
    }

    [Fact]
    public void Refuses_to_instrument_rimobs_internals() {
        AutoInstrumentPlan plan = Scan("RimWorks.RimObs.Library.Control.*");

        plan.Matched.Should().BeGreaterThan(0);
        plan.Targets.Should().BeEmpty();
        plan.SkippedBlocklisted.Should().BeGreaterThan(0);
    }

    // a second scope on a method the core pack already owns double-counts under Harmony and
    // overwrites the (target, At) handle under Concord.
    [Fact]
    public void Skips_a_method_an_existing_section_already_owns() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        try {
            MethodInfo owned = typeof(AutoTargets).GetMethod(nameof(AutoTargets.Worthwhile))!;
            SectionCatalog.RegisterDirect("test.auto_already", owned);

            AutoInstrumentPlan plan = Scan("RimObsTest.AutoFixtures.AutoTargets");

            plan.SkippedAlreadyInstrumented.Should().Be(1);
            plan.Targets.Select(m => m.Name).Should().NotContain("Worthwhile");
        }
        finally {
            SectionCatalog.Clear();
            SectionRegistry.Clear();
        }
    }

    [Fact]
    public void Counts_matches_past_the_cap_but_never_queues_them() {
        AutoInstrumentPlan plan = Scan("RimObsTest.AutoFixtures.AutoTargets", maxTargets: 1);

        plan.Matched.Should().Be(4);
        plan.Targets.Should().HaveCount(1);
        plan.SkippedOverCap.Should().Be(2);
    }

    [Fact]
    public void An_ignore_line_beats_an_include_that_also_matches() {
        AutoInstrumentPlan plan = Scan(
            "RimObsTest.AutoFixtures.AutoTargets\n!RimObsTest.AutoFixtures.*::Also*");

        plan.SkippedIgnored.Should().Be(1);
        plan.Matched.Should().Be(3);
        plan.Targets.Select(m => m.Name).Should().NotContain("AlsoWorthwhile");
    }

    [Fact]
    public void An_ignore_line_wins_even_when_it_is_listed_first() {
        AutoInstrumentPlan plan = Scan(
            "!RimObsTest.AutoFixtures.*::Also*\nRimObsTest.AutoFixtures.AutoTargets");

        plan.Targets.Select(m => m.Name).Should().NotContain("AlsoWorthwhile");
    }

    [Fact]
    public void Ignore_lines_alone_match_nothing() {
        AutoInstrumentPlan plan = Scan("!RimObsTest.AutoFixtures.*");

        plan.Matched.Should().Be(0);
        plan.Targets.Should().BeEmpty();
    }

    [Fact]
    public void An_ignore_line_can_carve_a_type_out_of_a_namespace_wildcard() {
        AutoInstrumentPlan plan = Scan(
            "RimObsTest.AutoFixtures.*\n!RimObsTest.AutoFixtures.OtherAutoTargets");

        plan.Targets.Select(m => m.Name).Should().NotContain("Elsewhere");
        plan.Targets.Select(m => m.Name).Should().Contain("Worthwhile");
    }
}
