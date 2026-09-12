using System;
using System.Diagnostics;
using System.Threading;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class SpinClockTests : IDisposable {
    public SpinClockTests() {
        SpinClock.ResetForTests();
    }

    public void Dispose() {
        SpinClock.ResetForTests();
    }

    [Fact]
    public void Falls_back_to_the_stopwatch_when_not_started() {
        SpinClock.IsSpinning.Should().BeFalse();

        long before = Stopwatch.GetTimestamp();
        long t = SpinClock.Timestamp();
        long after = Stopwatch.GetTimestamp();

        t.Should().BeInRange(before, after);
    }

    [Fact]
    public void Spinning_publishes_the_stopwatch_domain() {
        SpinClock.MinCores = 1;
        SpinClock.Start();
        SpinClock.IsSpinning.Should().BeTrue();
        Thread.Sleep(50);

        long t = SpinClock.Timestamp();
        long real = Stopwatch.GetTimestamp();

        // same domain: the published value trails the real clock by at most a few ms of spin lag.
        (real - t).Should().BeInRange(0, Stopwatch.Frequency / 100);
    }

    [Fact]
    public void Spinning_timestamps_never_go_backwards() {
        SpinClock.MinCores = 1;
        SpinClock.Start();
        Thread.Sleep(20);

        long last = SpinClock.Timestamp();
        for (int i = 0; i < 1_000_000; i++) {
            long t = SpinClock.Timestamp();
            t.Should().BeGreaterThanOrEqualTo(last);
            last = t;
        }
    }

    [Theory]
    [InlineData("600000 100000", 6)]
    [InlineData("max 100000", 0)]
    [InlineData("150000 100000", 1)]
    [InlineData("garbage", 0)]
    [InlineData("100000 0", 0)]
    public void ParseCpuMax_reads_the_cgroup_quota_as_whole_cpus(string text, int cores) {
        SpinClock.ParseCpuMax(text).Should().Be(cores);
    }

    [Fact]
    public void Refuses_to_start_below_the_core_gate() {
        SpinClock.MinCores = int.MaxValue;

        SpinClock.Start();

        SpinClock.IsSpinning.Should().BeFalse();
    }

    [Fact]
    public void Verify_disables_after_sustained_staleness() {
        SpinClock.MinCores = 1;
        SpinClock.Start();
        Thread.Sleep(20);

        SpinClock.s_TestFreeze = true;
        Thread.Sleep(20);
        for (int i = 0; i < SpinClock.MaxStrikes; i++)
            SpinClock.Verify();

        SpinClock.IsSpinning.Should().BeFalse();
        SpinClock.IsDisabled.Should().BeTrue();
    }

    [Fact]
    public void A_disabled_clock_never_restarts_and_still_tells_time() {
        SpinClock.MinCores = 1;
        SpinClock.Start();
        SpinClock.s_TestFreeze = true;
        Thread.Sleep(20);
        for (int i = 0; i < SpinClock.MaxStrikes; i++)
            SpinClock.Verify();
        SpinClock.IsDisabled.Should().BeTrue();

        SpinClock.Start();

        SpinClock.IsSpinning.Should().BeFalse();
        long before = Stopwatch.GetTimestamp();
        SpinClock.Timestamp().Should().BeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void A_healthy_verify_clears_earlier_strikes() {
        SpinClock.MinCores = 1;
        SpinClock.Start();
        Thread.Sleep(20);

        SpinClock.s_TestFreeze = true;
        Thread.Sleep(20);
        for (int i = 0; i < SpinClock.MaxStrikes - 1; i++)
            SpinClock.Verify();

        SpinClock.s_TestFreeze = false;
        Thread.Sleep(20);
        SpinClock.Verify();

        SpinClock.s_TestFreeze = true;
        Thread.Sleep(20);
        for (int i = 0; i < SpinClock.MaxStrikes - 1; i++)
            SpinClock.Verify();

        SpinClock.IsSpinning.Should().BeTrue("strikes must reset after a healthy pass");
    }
}
