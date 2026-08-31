using System.Threading;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class TpsFpsObserverTests {
    public TpsFpsObserverTests() {
        FrameTickCounters.Reset();
    }

    [Fact]
    public void Poll_with_no_increments_reports_zero_rates() {
        TpsFpsObserver observer = new();
        Thread.Sleep(20);

        bool ok = observer.TryPoll(out TpsFpsSample sample);

        ok.Should().BeTrue();
        sample.Tps.Should().Be(0.0);
        sample.Fps.Should().Be(0.0);
        sample.Tick.Should().Be(0);
    }

    [Fact]
    public void Poll_after_increments_reports_positive_rates_and_tick_count() {
        TpsFpsObserver observer = new();
        for (int i = 0; i < 60; i++)
            FrameTickCounters.RecordTick();
        for (int i = 0; i < 144; i++)
            FrameTickCounters.RecordFrame();
        Thread.Sleep(50);

        bool ok = observer.TryPoll(out TpsFpsSample sample);

        ok.Should().BeTrue();
        sample.Tps.Should().BeGreaterThan(0.0);
        sample.Fps.Should().BeGreaterThan(sample.Tps);
        sample.Tick.Should().Be(60);
    }

    [Fact]
    public void Successive_polls_only_count_new_increments() {
        TpsFpsObserver observer = new();
        for (int i = 0; i < 30; i++)
            FrameTickCounters.RecordTick();
        Thread.Sleep(20);
        observer.TryPoll(out _);

        Thread.Sleep(20);
        bool ok = observer.TryPoll(out TpsFpsSample sample);

        ok.Should().BeTrue();
        sample.Tps.Should().Be(0.0);
        sample.Tick.Should().Be(30);
    }
}
