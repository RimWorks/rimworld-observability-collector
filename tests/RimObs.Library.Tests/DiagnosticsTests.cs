using System;
using System.Linq;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Metrics;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class DiagnosticsTests : IDisposable {
    private const string TestPackageId = "RimWorks.RimObs.tests";

    public DiagnosticsTests() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
        Diagnostics.Reset();
        OwnerRegistry.RegisterMod(typeof(DiagnosticsTests).Assembly, TestPackageId);
    }

    public void Dispose() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
        Diagnostics.Reset();
    }

    [Fact]
    public void SamplesDroppedExternal_starts_at_zero() {
        Diagnostics.SamplesDroppedExternal.Should().Be(0);
    }

    [Fact]
    public void IncrementSamplesDropped_default_adds_one() {
        Diagnostics.IncrementSamplesDropped();
        Diagnostics.IncrementSamplesDropped();
        Diagnostics.IncrementSamplesDropped();

        Diagnostics.SamplesDroppedExternal.Should().Be(3);
    }

    [Fact]
    public void IncrementSamplesDropped_with_count_adds_that_amount() {
        Diagnostics.IncrementSamplesDropped(50);
        Diagnostics.IncrementSamplesDropped(25);

        Diagnostics.SamplesDroppedExternal.Should().Be(75);
    }

    [Fact]
    public void Reset_zeros_samples_dropped() {
        Diagnostics.IncrementSamplesDropped(100);

        Diagnostics.Reset();

        Diagnostics.SamplesDroppedExternal.Should().Be(0);
    }

    [Fact]
    public void CardinalityIncidentsTotal_is_zero_when_no_metrics() {
        Diagnostics.CardinalityIncidentsTotal.Should().Be(0);
    }

    [Fact]
    public void CardinalityIncidentsTotal_is_zero_when_metrics_under_limit() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("under_limit");
        Obs.Metrics.Add(handle, 1, "k", "a");
        Obs.Metrics.Add(handle, 1, "k", "b");

        Diagnostics.CardinalityIncidentsTotal.Should().Be(0);
    }

    [Fact]
    public void CardinalityIncidentsTotal_sums_incidents_across_metrics() {
        CounterHandle h1 = Obs.Metrics.RegisterCounter("noisy1", cardinalityLimit: 1);
        CounterHandle h2 = Obs.Metrics.RegisterCounter("noisy2", cardinalityLimit: 1);

        Obs.Metrics.Add(h1, 1, "k", "a");
        Obs.Metrics.Add(h1, 1, "k", "b");
        Obs.Metrics.Add(h1, 1, "k", "c");

        Obs.Metrics.Add(h2, 1, "k", "x");
        Obs.Metrics.Add(h2, 1, "k", "y");

        Diagnostics.CardinalityIncidentsTotal.Should().Be(3);
    }

    [Fact]
    public void GetMetricsWithIncidents_returns_empty_when_no_incidents() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("clean");
        Obs.Metrics.Add(handle, 1, "k", "a");

        Diagnostics.GetMetricsWithIncidents().Should().BeEmpty();
    }

    [Fact]
    public void GetMetricsWithIncidents_lists_only_metrics_with_incidents() {
        CounterHandle clean = Obs.Metrics.RegisterCounter("clean");
        CounterHandle noisy = Obs.Metrics.RegisterCounter("noisy", cardinalityLimit: 1);

        Obs.Metrics.Add(clean, 1, "k", "a");
        Obs.Metrics.Add(noisy, 1, "k", "a");
        Obs.Metrics.Add(noisy, 1, "k", "b");
        Obs.Metrics.Add(noisy, 1, "k", "c");

        var incidents = Diagnostics.GetMetricsWithIncidents().ToList();

        incidents.Should().HaveCount(1);
        incidents[0].MetricName.Should().Be(TestPackageId + ".noisy");
        incidents[0].OwnerPackageId.Should().Be(TestPackageId);
        incidents[0].CardinalityLimit.Should().Be(1);
        incidents[0].IncidentCount.Should().Be(2);
    }

    [Fact]
    public void Reset_zeros_cardinality_incidents() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("noisy", cardinalityLimit: 1);
        Obs.Metrics.Add(handle, 1, "k", "a");
        Obs.Metrics.Add(handle, 1, "k", "b");
        Obs.Metrics.Add(handle, 1, "k", "c");

        Diagnostics.CardinalityIncidentsTotal.Should().Be(2);

        Diagnostics.Reset();

        Diagnostics.CardinalityIncidentsTotal.Should().Be(0);
        Diagnostics.GetMetricsWithIncidents().Should().BeEmpty();
    }
}
