using System;
using FluentAssertions;
using RimWorks.RimObs.Profile;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Auto;

public sealed class AutoMuteTests : IDisposable {
    private readonly SectionHandle _cheap;
    private readonly SectionHandle _costly;

    public AutoMuteTests() {
        SectionRegistry.Clear();
        AutoMute.ResetForTests();
        _cheap = SectionRegistry.Register("test.automute_cheap");
        _costly = SectionRegistry.Register("test.automute_costly");
        AutoMute.Arm();
    }

    public void Dispose() {
        AutoMute.ResetForTests();
        SectionRegistry.Clear();
    }

    [Fact]
    public void Arming_sets_a_budget_from_the_stopwatch_frequency() {
        AutoMute.BudgetTicks.Should().Be(
            System.Diagnostics.Stopwatch.Frequency * AutoMute.ScopeOverheadNanos * AutoMute.SampleCount / 1_000_000_000L);
    }

    [Fact]
    public void Mutes_a_watched_section_that_never_beats_the_overhead() {
        AutoMute.Watch(_cheap.Id);

        Observe(_cheap.Id, 0, AutoMute.SampleCount);

        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();
        AutoMute.MutedCount.Should().Be(1);
    }

    [Fact]
    public void Keeps_a_watched_section_that_costs_real_time() {
        AutoMute.Watch(_costly.Id);

        Observe(_costly.Id, AutoMute.BudgetTicks + 1, AutoMute.SampleCount);

        SectionRegistry.IsActive(_costly.Id).Should().BeTrue();
        AutoMute.MutedCount.Should().Be(0);
    }

    [Fact]
    public void Judges_a_section_once_and_never_again() {
        AutoMute.Watch(_cheap.Id);

        Observe(_cheap.Id, 0, AutoMute.SampleCount * 3);

        AutoMute.JudgedCount.Should().Be(1);
    }

    [Fact]
    public void Stays_silent_before_the_sample_size() {
        AutoMute.Watch(_cheap.Id);

        Observe(_cheap.Id, 0, AutoMute.SampleCount - 1);

        SectionRegistry.IsActive(_cheap.Id).Should().BeTrue();
        AutoMute.JudgedCount.Should().Be(0);
    }

    [Fact]
    public void Disabled_still_judges_but_never_mutes() {
        AutoMute.Enabled = false;
        AutoMute.Watch(_cheap.Id);

        Observe(_cheap.Id, 0, AutoMute.SampleCount);

        SectionRegistry.IsActive(_cheap.Id).Should().BeTrue();
        AutoMute.MutedCount.Should().Be(0);
        AutoMute.JudgedCount.Should().Be(1);
    }

    // manually registered and core-pack sections are never handed to Watch, so a cheap one
    // the author asked for must survive.
    [Fact]
    public void Never_mutes_a_section_it_was_not_told_to_watch() {
        Observe(_cheap.Id, 0, AutoMute.SampleCount * 2);

        SectionRegistry.IsActive(_cheap.Id).Should().BeTrue();
        AutoMute.MutedCount.Should().Be(0);
    }

    private static void Observe(int sectionId, long selfTicks, int times) {
        for (int i = 0; i < times; i++)
            AutoMute.Observe(sectionId, selfTicks);
    }

    private static long TicksPerUs => System.Diagnostics.Stopwatch.Frequency / 1_000_000L;

    // "instrument everything under a budget": the judge mutes the cheapest chatty sections
    // until the projected per-frame scope tax fits, and leaves the expensive ones measuring.
    [Fact]
    public void Budget_judge_mutes_the_cheapest_chatter_until_the_frame_budget_fits() {
        AutoMute.BudgetUsPerFrame = 1;
        AutoMute.Watch(_cheap.Id);
        AutoMute.Watch(_costly.Id);
        // 75ns per call: 20k cheap calls project ~15us/frame over 100 frames; way over 1us.
        Observe(_cheap.Id, 1, 20_000);
        Observe(_costly.Id, TicksPerUs * 500, 10);

        AutoMute.JudgeBudget(framesElapsed: 100);

        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();
        SectionRegistry.IsActive(_costly.Id).Should().BeTrue();
    }

    [Fact]
    public void Budget_judge_leaves_a_worthwhile_section_alone_while_the_tax_fits() {
        AutoMute.BudgetUsPerFrame = 1000;
        AutoMute.Watch(_costly.Id);
        // expensive per call so the force-judge keeps it, few calls so the tax fits.
        Observe(_costly.Id, TicksPerUs * 500, AutoMute.SampleCount - 1);

        AutoMute.JudgeBudget(framesElapsed: 100);

        SectionRegistry.IsActive(_costly.Id).Should().BeTrue();
    }

    [Fact]
    public void Budget_zero_disables_the_judge() {
        AutoMute.BudgetUsPerFrame = 0;
        AutoMute.Watch(_costly.Id);
        // heavy enough that the one-shot keeps it; only the budget judge could mute it.
        Observe(_costly.Id, TicksPerUs * 500, 50_000);

        AutoMute.JudgeBudget(framesElapsed: 1);

        SectionRegistry.IsActive(_costly.Id).Should().BeTrue();
    }

    // the 30s config poll used to call ApplyDisabledSet and flip every judge-muted section
    // back on, so cheap leaves kept sampling forever.
    [Fact]
    public void A_config_poll_does_not_unmute_a_budget_muted_section() {
        AutoMute.BudgetUsPerFrame = 1;
        AutoMute.Watch(_cheap.Id);
        Observe(_cheap.Id, 1, 20_000);
        AutoMute.JudgeBudget(framesElapsed: 100);
        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();

        SectionRegistry.ApplyDisabledSet([]);

        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();
    }

    [Fact]
    public void A_config_poll_does_not_unmute_a_one_shot_muted_section() {
        AutoMute.Watch(_cheap.Id);
        Observe(_cheap.Id, 0, AutoMute.SampleCount);
        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();

        SectionRegistry.ApplyDisabledSet([]);

        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();
    }

    [Fact]
    public void An_explicit_re_enable_clears_the_auto_mute() {
        AutoMute.Watch(_cheap.Id);
        Observe(_cheap.Id, 0, AutoMute.SampleCount);
        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();

        SectionRegistry.SetActive(_cheap.Id, true);
        SectionRegistry.ApplyDisabledSet([]);

        SectionRegistry.IsActive(_cheap.Id).Should().BeTrue();
    }

    private sealed class CountingSink : ISampleSink {
        public int Count;

        public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
            Count++;
        }
    }

    // pre-judgment samples were 65k x 64 calls of pure flood; the judge never needed them.
    [Fact]
    public void A_pending_section_sends_nothing_until_it_survives_judgment() {
        CountingSink sink = new();
        Profiler.SetSink(sink);
        try {
            AutoMute.Watch(_costly.Id);
            // prime the thread state; a bare StopById never creates it and the fold needs it.
            Profiler.EnterById(_costly.Id);
            Profiler.ExitById(_costly.Id);
            // a synthetic old token makes every call read as expensive real work.
            long back = AutoMute.BudgetTicks * 10;

            for (int i = 0; i < AutoMute.SampleCount / 2; i++)
                Profiler.StopById(_costly.Id, System.Diagnostics.Stopwatch.GetTimestamp() - back);
            int preJudgment = sink.Count;

            AutoMute.JudgeBudget(framesElapsed: 100);
            Profiler.StopById(_costly.Id, System.Diagnostics.Stopwatch.GetTimestamp() - back);

            preJudgment.Should().Be(0, "pending sections must not reach the sink");
            sink.Count.Should().BeGreaterThan(0, "a surviving section reports after judgment");
        }
        finally {
            Profiler.SetSink(null);
        }
    }

    [Fact]
    public void A_straggler_is_force_judged_at_the_window_with_what_accrued() {
        AutoMute.Watch(_cheap.Id);
        Observe(_cheap.Id, 0, AutoMute.ForcedJudgeCallFloor + 2);

        AutoMute.JudgeBudget(framesElapsed: 100);

        AutoMute.IsPending(_cheap.Id).Should().BeFalse();
        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse("zero-cost calls past the floor prorate under the budget");
    }

    // one or two accrued calls prove nothing; the section unhides and the one-shot decides later.
    [Fact]
    public void Below_the_call_floor_a_straggler_unhides_without_a_verdict() {
        AutoMute.Watch(_cheap.Id);
        Observe(_cheap.Id, 0, AutoMute.ForcedJudgeCallFloor - 1);

        AutoMute.JudgeBudget(framesElapsed: 100);

        AutoMute.IsPending(_cheap.Id).Should().BeFalse();
        SectionRegistry.IsActive(_cheap.Id).Should().BeTrue();
    }

    // the judge runs every window on the sender thread; a fresh list + closure sort there was
    // a once-a-second gc trigger under boehm.
    [Fact]
    public void Budget_judge_allocates_nothing_per_pass() {
        AutoMute.BudgetUsPerFrame = 1;
        AutoMute.Watch(_cheap.Id);
        AutoMute.Watch(_costly.Id);
        Observe(_cheap.Id, 1, 32);
        Observe(_costly.Id, TicksPerUs * 500, 10);
        AutoMute.JudgeBudget(framesElapsed: 100);

        Observe(_costly.Id, TicksPerUs * 500, 10);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            AutoMute.JudgeBudget(framesElapsed: 100);
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0);
    }

    [Fact]
    public void Budget_judge_starts_a_fresh_window_each_pass() {
        AutoMute.BudgetUsPerFrame = 1;
        AutoMute.Watch(_cheap.Id);
        Observe(_cheap.Id, 1, 20_000);
        AutoMute.JudgeBudget(framesElapsed: 100);
        SectionRegistry.IsActive(_cheap.Id).Should().BeFalse();

        AutoMute.Watch(_costly.Id);
        Observe(_costly.Id, TicksPerUs * 500, 4);

        // the old window's 20k calls must not count against the new pass.
        AutoMute.JudgeBudget(framesElapsed: 100);
        SectionRegistry.IsActive(_costly.Id).Should().BeTrue();
    }
}
