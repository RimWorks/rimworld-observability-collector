using System;
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
            "RimObsTest.AutoFixtures.AutoTargets::ThirdWorthwhile", autoMute: false, "test.owner", s_Here);

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

    private static AutoInstrumentPlan Apply(string filters) =>
        AutoInstrumentRunner.ApplyFilters(filters, autoMute: true, "test.owner", s_Here);
}
