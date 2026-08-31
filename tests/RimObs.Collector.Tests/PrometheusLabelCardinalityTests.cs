using RimWorks.RimObs.Collector.Exporters;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class PrometheusLabelCardinalityTests {
    [Fact]
    public void Values_within_limit_pass_through_unchanged() {
        PrometheusLabelCardinality guard = new(3);
        guard.Resolve("a").Should().Be("a");
        guard.Resolve("b").Should().Be("b");
        guard.Resolve("c").Should().Be("c");
        guard.OverflowCount.Should().Be(0);
    }

    [Fact]
    public void Values_beyond_limit_collapse_to_other() {
        PrometheusLabelCardinality guard = new(2);
        guard.Resolve("a").Should().Be("a");
        guard.Resolve("b").Should().Be("b");
        guard.Resolve("c").Should().Be(PrometheusLabelCardinality.OverflowValue);
        guard.Resolve("d").Should().Be(PrometheusLabelCardinality.OverflowValue);
        guard.OverflowCount.Should().Be(2);
    }

    [Fact]
    public void Repeated_known_value_does_not_consume_budget() {
        PrometheusLabelCardinality guard = new(2);
        guard.Resolve("a");
        guard.Resolve("a");
        guard.Resolve("b").Should().Be("b", "repeats of an already-seen value must not exhaust the limit");
    }

    [Fact]
    public void Limit_below_one_is_clamped_to_one() {
        PrometheusLabelCardinality guard = new(0);
        guard.Resolve("a").Should().Be("a");
        guard.Resolve("b").Should().Be(PrometheusLabelCardinality.OverflowValue);
    }
}
