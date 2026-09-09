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
}
