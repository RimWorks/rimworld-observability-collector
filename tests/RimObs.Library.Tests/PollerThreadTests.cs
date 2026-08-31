using System;
using System.Threading;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class PollerThreadTests {
    [Fact]
    public void Ctor_rejects_invalid_arguments() {
        Action emptyName = () => _ = new PollerThread("", () => { }, 10);
        Action nullTick = () => _ = new PollerThread("p", null!, 10);
        Action badInterval = () => _ = new PollerThread("p", () => { }, 0);

        emptyName.Should().Throw<ArgumentException>();
        nullTick.Should().Throw<ArgumentNullException>();
        badInterval.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Throwing_tick_does_not_kill_the_poll_loop() {
        int ticks = 0;
        PollerThread poller = new("RimObs.Test.Throwing", () => {
            Interlocked.Increment(ref ticks);
            throw new InvalidOperationException("boom");
        }, intervalMs: 5);

        poller.Start();
        try {
            SpinUntil(() => Volatile.Read(ref ticks) >= 3, TimeSpan.FromSeconds(2));
            Volatile.Read(ref ticks).Should().BeGreaterThanOrEqualTo(3);
            poller.IsRunning.Should().BeTrue();
        }
        finally {
            poller.Stop();
        }
    }

    [Fact]
    public void Start_is_idempotent_and_runs_a_single_loop() {
        int ticks = 0;
        PollerThread poller = new("RimObs.Test.Idempotent", () => Interlocked.Increment(ref ticks), intervalMs: 5);

        poller.Start();
        poller.Start();
        try {
            poller.IsRunning.Should().BeTrue();
            SpinUntil(() => Volatile.Read(ref ticks) >= 1, TimeSpan.FromSeconds(2));
        }
        finally {
            poller.Stop();
        }
    }

    [Fact]
    public void Stop_halts_ticking_and_is_idempotent() {
        int ticks = 0;
        PollerThread poller = new("RimObs.Test.Stop", () => Interlocked.Increment(ref ticks), intervalMs: 5);

        poller.Start();
        SpinUntil(() => Volatile.Read(ref ticks) >= 1, TimeSpan.FromSeconds(2));

        poller.Stop();
        poller.IsRunning.Should().BeFalse();

        int afterStop = Volatile.Read(ref ticks);
        Thread.Sleep(50);
        Volatile.Read(ref ticks).Should().Be(afterStop);

        Action stopAgain = () => poller.Stop();
        stopAgain.Should().NotThrow();
    }

    [Fact]
    public void Stopped_poller_can_be_restarted() {
        int ticks = 0;
        PollerThread poller = new("RimObs.Test.Restart", () => Interlocked.Increment(ref ticks), intervalMs: 5);

        poller.Start();
        poller.Stop();
        int afterFirstStop = Volatile.Read(ref ticks);

        poller.Start();
        try {
            SpinUntil(() => Volatile.Read(ref ticks) > afterFirstStop, TimeSpan.FromSeconds(2));
            Volatile.Read(ref ticks).Should().BeGreaterThan(afterFirstStop);
        }
        finally {
            poller.Stop();
        }
    }

    private static void SpinUntil(Func<bool> condition, TimeSpan timeout) {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (condition())
                return;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Condition never became true within the timeout.");
    }
}
