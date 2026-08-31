using RimWorks.RimObs.Collector.Hosting;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class LifecycleDecisionTests {
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void ShouldShutdown_is_false_when_parent_not_tracked() {
        bool result = LifecycleDecision.ShouldShutdown(parentTracked: false, parentAlive: false, sinceLastActivity: TimeSpan.FromHours(1), idleTimeout: IdleTimeout);
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldShutdown_is_true_when_parent_dead() {
        bool result = LifecycleDecision.ShouldShutdown(parentTracked: true, parentAlive: false, sinceLastActivity: TimeSpan.Zero, idleTimeout: IdleTimeout);
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldShutdown_is_true_when_idle_exceeded() {
        bool result = LifecycleDecision.ShouldShutdown(parentTracked: true, parentAlive: true, sinceLastActivity: TimeSpan.FromMinutes(6), idleTimeout: IdleTimeout);
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldShutdown_is_false_when_alive_and_within_idle() {
        bool result = LifecycleDecision.ShouldShutdown(parentTracked: true, parentAlive: true, sinceLastActivity: TimeSpan.FromMinutes(1), idleTimeout: IdleTimeout);
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldShutdown_ignores_idle_when_timeout_is_zero() {
        bool result = LifecycleDecision.ShouldShutdown(parentTracked: true, parentAlive: true, sinceLastActivity: TimeSpan.FromHours(1), idleTimeout: TimeSpan.Zero);
        result.Should().BeFalse();
    }
}
