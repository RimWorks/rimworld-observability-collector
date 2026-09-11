using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using RimObsTest.AutoFixtures;
using RimWorks.RimObs.Auto;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Tests;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Auto;

public sealed class AutoInstrumentRunnerTests : IDisposable {
    private static readonly Assembly[] s_Here = [typeof(AutoTargets).Assembly];

    private readonly RecordingSink _sink = new();

    public AutoInstrumentRunnerTests() {
        TestBackend.Activate();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        PatchRegistry.ResetForTests();
        AutoInstrumentRunner.ResetForTests();
        AutoMute.ResetForTests();
        Profiler.SetSink(_sink);
        Profiler.Enabled = true;
    }

    public void Dispose() {
        Profiler.SetSink(null);
        AutoInstrumentRunner.ResetForTests();
        AutoMute.ResetForTests();
        PatchRegistry.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        TestBackend.Deactivate();
    }

    [Fact]
    public void Summary_reads_off_when_nothing_was_applied() {
        AutoInstrumentRunner.BuildSummary().Should().Be("off");
    }

    [Fact]
    public void ApplyFilters_queues_the_eligible_targets_without_patching_yet() {
        AutoInstrumentPlan plan = Apply("RimObsTest.AutoFixtures.AutoTargets");

        plan.Eligible.Should().Be(3);
        AutoInstrumentRunner.Pending.Should().Be(3);
        AutoInstrumentRunner.Instrumented.Should().Be(0);
    }

    [Fact]
    public void Pump_patches_the_queue_and_the_scope_then_records() {
        Apply("RimObsTest.AutoFixtures.AutoTargets::Worthwhile");
        Drain();

        AutoTargets.Worthwhile(4);

        AutoInstrumentRunner.Instrumented.Should().Be(1);
        AutoInstrumentRunner.Pending.Should().Be(0);
        _sink.Samples.Should().HaveCount(1);
    }

    [Fact]
    public void Pump_on_an_empty_queue_does_nothing() {
        Action act = AutoInstrumentRunner.Pump;

        act.Should().NotThrow();
        AutoInstrumentRunner.Instrumented.Should().Be(0);
    }

    [Fact]
    public void Section_name_carries_the_owner_and_the_dynamic_marker() {
        Apply("RimObsTest.AutoFixtures.AutoTargets::AlsoWorthwhile");
        Drain();

        SectionRegistry.GetName(0).Should().StartWith("test.owner.dynamic.");
    }

    [Fact]
    public void Applying_a_filter_arms_auto_mute() {
        AutoMute.Armed.Should().BeFalse();

        Apply("RimObsTest.AutoFixtures.AutoTargets::ThirdWorthwhile");

        AutoMute.Armed.Should().BeTrue();
        AutoMute.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Auto_mute_off_still_arms_but_leaves_the_decision_disabled() {
        AutoInstrumentRunner.ApplyFilters(
            "RimObsTest.AutoFixtures.AutoTargets::ThirdWorthwhile", ignore: null, autoMute: false, "test.owner", s_Here);

        AutoMute.Armed.Should().BeTrue();
        AutoMute.Enabled.Should().BeFalse();
    }

    [Fact]
    public void A_filter_matching_nothing_leaves_the_queue_empty() {
        AutoInstrumentPlan plan = Apply("Nope.Nothing.Here.*");

        plan.Matched.Should().Be(0);
        AutoInstrumentRunner.Pending.Should().Be(0);
        AutoMute.Armed.Should().BeFalse();
    }

    [Fact]
    public void Summary_reports_every_count() {
        Apply("RimObsTest.AutoFixtures.AutoTargets");
        Drain();

        AutoInstrumentRunner.BuildSummary().Should().Be(
            "3 instrumented, 0 muted, 1 trivial, 0 other, 0 refused, 0 pending of 4 matched");
    }

    // the pump has to survive a target the backend refuses, or one bad method stops the run.
    [Fact]
    public void A_refused_target_is_counted_and_the_run_continues() {
        Apply("RimObsTest.AutoFixtures.AutoTargets");
        SectionRegistry.Clear();
        for (int i = 0; i < SectionRegistry.MaxSections; i++)
            SectionRegistry.Register("filler." + i);

        Drain();

        AutoInstrumentRunner.Refused.Should().Be(3);
        AutoInstrumentRunner.Instrumented.Should().Be(0);
        AutoInstrumentRunner.Pending.Should().Be(0);
    }

    private static void Drain() {
        while (AutoInstrumentRunner.Pending > 0)
            AutoInstrumentRunner.Pump();
        AutoInstrumentRunner.Pump();
    }

    // the whole point of the preview is that the count the dashboard shows is the count that
    // gets applied, so these assert Preview against ApplyFilters on the same filters.
    [Fact]
    public void Preview_reports_the_same_counts_ApplyFilters_would() {
        AutoInstrumentPlan preview = AutoInstrumentRunner.Preview(
            "RimObsTest.AutoFixtures.AutoTargets", ignore: null, s_Here);

        AutoInstrumentPlan applied = Apply("RimObsTest.AutoFixtures.AutoTargets");

        preview.Matched.Should().Be(applied.Matched);
        preview.Eligible.Should().Be(applied.Eligible);
        preview.SkippedTrivial.Should().Be(applied.SkippedTrivial);
    }

    [Fact]
    public void Preview_patches_nothing_and_queues_nothing() {
        AutoInstrumentRunner.Preview("RimObsTest.AutoFixtures.AutoTargets", ignore: null, s_Here);

        AutoInstrumentRunner.Pending.Should().Be(0);
        AutoInstrumentRunner.Instrumented.Should().Be(0);
        AutoInstrumentRunner.Matched.Should().Be(0);
    }

    [Fact]
    public void Preview_counts_an_ignore_line_as_skipped_rather_than_eligible() {
        AutoInstrumentPlan wide = AutoInstrumentRunner.Preview(
            "RimObsTest.AutoFixtures.AutoTargets", ignore: null, s_Here);

        AutoInstrumentPlan narrowed = AutoInstrumentRunner.Preview(
            "RimObsTest.AutoFixtures.AutoTargets",
            "RimObsTest.AutoFixtures.AutoTargets::Worthwhile",
            s_Here);

        narrowed.SkippedIgnored.Should().BeGreaterThan(0);
        narrowed.Eligible.Should().BeLessThan(wide.Eligible);
    }

    [Fact]
    public void Preview_on_an_empty_filter_matches_nothing() {
        AutoInstrumentPlan plan = AutoInstrumentRunner.Preview(null, null, s_Here);

        plan.Matched.Should().Be(0);
        plan.Eligible.Should().Be(0);
    }

    [Fact]
    public void Preview_honours_a_cap_the_caller_passed() {
        AutoInstrumentPlan plan = AutoInstrumentRunner.Preview(
            "RimObsTest.AutoFixtures.AutoTargets", ignore: null, s_Here, maxTargets: 1);

        plan.Eligible.Should().Be(1);
        plan.Truncated.Should().BeTrue();
        plan.SkippedOverCap.Should().Be(2);
    }

    [Fact]
    public void ApplyFilters_honours_the_configured_cap_and_reports_the_truncation() {
        AutoInstrumentRunner.MaxTargets = 1;

        AutoInstrumentPlan plan = Apply("RimObsTest.AutoFixtures.AutoTargets");

        plan.Eligible.Should().Be(1);
        AutoInstrumentRunner.Truncated.Should().BeTrue();
        AutoInstrumentRunner.SkippedOverCap.Should().Be(2);
    }

    [Fact]
    public void The_cap_falls_back_to_the_default_when_it_is_set_to_nothing() {
        AutoInstrumentRunner.MaxTargets = 0;

        AutoInstrumentRunner.MaxTargets.Should().Be(AutoInstrumentScanner.DefaultMaxTargets);
    }

    // a cap change on its own has to force a rescan, or the dashboard raises the cap and
    // nothing happens until the filters also change.
    [Fact]
    public void Changing_the_cap_re_arms_a_settings_snapshot_already_taken() {
        AutoInstrumentRequest.Set(enabled: true, filters: "Verse.*", ignore: null, muteTrivial: true);
        AutoInstrumentRequest.TryTake(out bool _, out string _, out string _, out bool _).Should().BeTrue();
        AutoInstrumentRequest.HasPending.Should().BeFalse();

        AutoInstrumentRunner.MaxTargets = 64;

        AutoInstrumentRequest.HasPending.Should().BeTrue();
    }

    private static AutoInstrumentPlan Apply(string filters) =>
        AutoInstrumentRunner.ApplyFilters(filters, ignore: null, autoMute: true, "test.owner", s_Here);

    // the config poll runs on its own thread, so it parks the settings and Pump applies them.
    // the scan itself cannot be asserted here: AssemblyIndex drops every RimObs.* assembly,
    // which is where the fixture types live. ApplyFilters is covered directly above instead.
    [Fact]
    public void Pump_consumes_a_parked_request_and_does_not_replay_it() {
        AutoInstrumentRequest.Set(
            enabled: true, filters: "Verse.*", ignore: null, muteTrivial: true);
        AutoInstrumentRequest.HasPending.Should().BeTrue();

        AutoInstrumentRunner.Pump();
        AutoInstrumentRequest.HasPending.Should().BeFalse();

        AutoInstrumentRunner.Pump();
        AutoInstrumentRequest.HasPending.Should().BeFalse();
    }

    // the enabled toggle used to collapse into an empty filter string, so off and on-with-no-
    // filters parked the same snapshot and the second one was deduped away.
    [Fact]
    public void Turning_the_filter_off_unpatches_what_it_applied() {
        Apply("RimObsTest.AutoFixtures.AutoTargets::Worthwhile");
        Drain();
        AutoInstrumentRunner.Instrumented.Should().Be(1);

        AutoInstrumentRequest.Set(enabled: false, filters: null, ignore: null, muteTrivial: true);
        Drain();

        PatchRegistry.Snapshot().Should().BeEmpty();
        _sink.Samples.Clear();
        AutoTargets.Worthwhile(4);
        _sink.Samples.Should().BeEmpty();
    }

    [Fact]
    public void An_unpatch_is_queued_for_the_budget_not_run_inline() {
        Apply("RimObsTest.AutoFixtures.AutoTargets");
        Drain();

        AutoInstrumentRunner.ApplyFilters(null, ignore: null, autoMute: true, "test.owner", s_Here);

        AutoInstrumentRunner.Pending.Should().Be(3);
        PatchRegistry.Snapshot().Should().HaveCount(3);

        Drain();
        PatchRegistry.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void A_narrower_filter_drops_only_what_stopped_matching() {
        Apply("RimObsTest.AutoFixtures.*");
        Drain();
        int keptId = PatchIdFor("Worthwhile");

        Apply("RimObsTest.AutoFixtures.AutoTargets::Worthwhile");
        Drain();

        PatchRegistry.Snapshot().Should().HaveCount(1);
        PatchIdFor("Worthwhile").Should().Be(keptId);
    }

    [Fact]
    public void Re_enabling_the_same_filter_patches_the_reverted_targets_again() {
        Apply("RimObsTest.AutoFixtures.AutoTargets::Worthwhile");
        Drain();
        AutoInstrumentRunner.ApplyFilters(null, ignore: null, autoMute: true, "test.owner", s_Here);
        Drain();
        PatchRegistry.Snapshot().Should().BeEmpty();

        Apply("RimObsTest.AutoFixtures.AutoTargets::Worthwhile");
        Drain();

        AutoInstrumentRunner.Instrumented.Should().Be(1);
        _sink.Samples.Clear();
        AutoTargets.Worthwhile(4);
        _sink.Samples.Should().HaveCount(1);
    }

    // a plan queued a frame ago must not keep patching after the user turned the filter off.
    [Fact]
    public void A_request_that_wants_nothing_drops_the_queue_before_it_patches() {
        Apply("RimObsTest.AutoFixtures.AutoTargets");
        AutoInstrumentRunner.Pending.Should().Be(3);

        AutoInstrumentRequest.Set(enabled: false, filters: null, ignore: null, muteTrivial: true);
        Drain();

        AutoInstrumentRunner.Instrumented.Should().Be(0);
        PatchRegistry.Snapshot().Should().BeEmpty();
    }

    private static int PatchIdFor(string methodName) =>
        PatchRegistry.Snapshot().Single(row => row.Signature.Contains(":" + methodName + "(")).Id;

    [Fact]
    public void Pump_with_nothing_parked_scans_nothing() {
        AutoInstrumentRunner.Pump();

        AutoInstrumentRunner.Matched.Should().Be(0);
        AutoInstrumentRunner.BuildSummary().Should().Be("off");
    }
}
