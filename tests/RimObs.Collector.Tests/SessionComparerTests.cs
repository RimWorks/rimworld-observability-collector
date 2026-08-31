using System.Linq;
using RimWorks.RimObs.Collector.Comparison;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class SessionComparerTests {
    private static SectionSnapshot Section(int id, string name, long samples, long totalNs) {
        return new SectionSnapshot(id, name, OwnerName.FromSection(name), samples, totalNs, 0, totalNs);
    }

    private static SessionSnapshot Snapshot(string id, string game, params SectionSnapshot[] sections) {
        return new SessionSnapshot(id, false, "1.0.0", game, 0, sections.ToList(), []);
    }

    [Fact]
    public void Compare_computes_total_timing_delta() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "modA_scan", 10, 1000));
        SessionSnapshot head = Snapshot("head", "1.5", Section(1, "modA_scan", 10, 1500));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Timing.BaseTotalNs.Should().Be(1000);
        result.Timing.HeadTotalNs.Should().Be(1500);
        result.Timing.DeltaNs.Should().Be(500);
        result.Timing.DeltaPercent.Should().BeApproximately(50.0, 0.001);
    }

    [Fact]
    public void Compare_flags_likely_regression_candidate_when_section_grows_significantly() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "modA_pawnscan", 10, 4_000_000));
        SessionSnapshot head = Snapshot("head", "1.5", Section(1, "modA_pawnscan", 10, 8_000_000));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        SectionDelta delta = result.Hotspots.Single(h => h.Name == "modA_pawnscan");
        delta.Status.Should().Be(DeltaStatus.Regressed);
        delta.LikelyRegressionCandidate.Should().BeTrue();
    }

    [Fact]
    public void Compare_does_not_flag_regression_for_tiny_absolute_change() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "modA_scan", 10, 100));
        SessionSnapshot head = Snapshot("head", "1.5", Section(1, "modA_scan", 10, 1000));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Hotspots.Single().LikelyRegressionCandidate.Should().BeFalse();
    }

    [Fact]
    public void Compare_marks_added_and_removed_sections() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "old_section", 5, 500));
        SessionSnapshot head = Snapshot("head", "1.5", Section(2, "new_section", 5, 500));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Hotspots.Single(h => h.Name == "old_section").Status.Should().Be(DeltaStatus.Removed);
        result.Hotspots.Single(h => h.Name == "new_section").Status.Should().Be(DeltaStatus.Added);
    }

    [Fact]
    public void Compare_aggregates_mod_cost_by_owner_prefix() {
        SessionSnapshot baseline = Snapshot("base", "1.5",
            Section(1, "modA_a", 1, 100),
            Section(2, "modA_b", 1, 200),
            Section(3, "modB_c", 1, 50));
        SessionSnapshot head = Snapshot("head", "1.5",
            Section(1, "modA_a", 1, 600),
            Section(2, "modA_b", 1, 200),
            Section(3, "modB_c", 1, 50));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        OwnerDelta modA = result.ModCosts.Single(m => m.Owner == "modA");
        modA.BaseTotalNs.Should().Be(300);
        modA.HeadTotalNs.Should().Be(800);
        modA.DeltaNs.Should().Be(500);
    }

    [Fact]
    public void Compare_produces_load_order_diff() {
        SessionSnapshot baseline = Snapshot("base", "1.5",
            Section(1, "modA_a", 1, 1),
            Section(2, "modB_b", 1, 1));
        SessionSnapshot head = Snapshot("head", "1.5",
            Section(1, "modA_a", 1, 1),
            Section(3, "modC_c", 1, 1));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.LoadOrder.Common.Should().ContainSingle().Which.Should().Be("modA");
        result.LoadOrder.Added.Should().ContainSingle().Which.Should().Be("modC");
        result.LoadOrder.Removed.Should().ContainSingle().Which.Should().Be("modB");
    }

    [Fact]
    public void Compare_warns_when_game_version_differs() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "modA_a", 1, 1));
        SessionSnapshot head = Snapshot("head", "1.6", Section(1, "modA_a", 1, 1));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Warnings.Should().Contain(w => w.Contains("Game version differs"));
    }

    [Fact]
    public void Compare_warns_when_mod_set_differs() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "modA_a", 1, 1));
        SessionSnapshot head = Snapshot("head", "1.5", Section(2, "modB_b", 1, 1));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Warnings.Should().Contain(w => w.Contains("Mod set differs"));
    }

    [Fact]
    public void Compare_warns_when_a_session_has_no_samples() {
        SessionSnapshot baseline = Snapshot("base", "1.5", Section(1, "modA_a", 0, 0));
        SessionSnapshot head = Snapshot("head", "1.5", Section(1, "modA_a", 10, 1000));

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Warnings.Should().Contain(w => w.Contains("no recorded section samples"));
    }
}
