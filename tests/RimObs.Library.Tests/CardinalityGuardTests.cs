using System;
using System.Reflection;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Metrics;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CardinalityGuardTests : IDisposable {
    private const string TestPackageId = "RimWorks.RimObs.tests";

    public CardinalityGuardTests() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
        OwnerRegistry.RegisterMod(typeof(CardinalityGuardTests).Assembly, TestPackageId);
    }

    public void Dispose() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
    }

    [Fact]
    public void Single_label_creates_separate_entries_per_value() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("requests");

        Obs.Metrics.Add(handle, 1, "outcome", "ok");
        Obs.Metrics.Add(handle, 1, "outcome", "ok");
        Obs.Metrics.Add(handle, 1, "outcome", "fail");

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.LabeledEntries["outcome=ok"].CounterTotal.Should().Be(2);
        descriptor.LabeledEntries["outcome=fail"].CounterTotal.Should().Be(1);
    }

    [Fact]
    public void Multi_label_tuples_canonicalize_consistently() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("events");

        Obs.Metrics.Add(handle, 1, ("phase", "init"), ("outcome", "ok"));
        Obs.Metrics.Add(handle, 1, ("phase", "init"), ("outcome", "ok"));

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.LabeledEntries["phase=init,outcome=ok"].CounterTotal.Should().Be(2);
    }

    [Fact]
    public void Excess_label_values_collapse_to_overflow() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("noisy", cardinalityLimit: 3);

        Obs.Metrics.Add(handle, 1, "id", "a");
        Obs.Metrics.Add(handle, 1, "id", "b");
        Obs.Metrics.Add(handle, 1, "id", "c");
        Obs.Metrics.Add(handle, 1, "id", "d");
        Obs.Metrics.Add(handle, 1, "id", "e");

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.LabeledEntries.ContainsKey("id=a").Should().BeTrue();
        descriptor.LabeledEntries.ContainsKey("id=b").Should().BeTrue();
        descriptor.LabeledEntries.ContainsKey("id=c").Should().BeTrue();
        descriptor.LabeledEntries.ContainsKey(MetricDescriptor.OverflowLabel).Should().BeTrue();
        descriptor.LabeledEntries[MetricDescriptor.OverflowLabel].CounterTotal.Should().Be(2);
        descriptor.CardinalityIncidentCount.Should().Be(2);
    }

    [Fact]
    public void Known_labels_after_overflow_still_increment_normally() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("mixed", cardinalityLimit: 2);

        Obs.Metrics.Add(handle, 1, "id", "a");
        Obs.Metrics.Add(handle, 1, "id", "b");
        Obs.Metrics.Add(handle, 1, "id", "overflow_one");
        Obs.Metrics.Add(handle, 1, "id", "a");

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.LabeledEntries["id=a"].CounterTotal.Should().Be(2);
        descriptor.LabeledEntries["id=b"].CounterTotal.Should().Be(1);
        descriptor.LabeledEntries[MetricDescriptor.OverflowLabel].CounterTotal.Should().Be(1);
        descriptor.CardinalityIncidentCount.Should().Be(1);
    }

    [Fact]
    public void Labeled_gauge_set_replaces_per_label_value() {
        GaugeHandle handle = Obs.Metrics.RegisterGauge("temp");

        Obs.Metrics.Set(handle, 10, "room", "kitchen");
        Obs.Metrics.Set(handle, 20, "room", "kitchen");
        Obs.Metrics.Set(handle, 99, "room", "bedroom");

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.LabeledEntries["room=kitchen"].GaugeValue.Should().Be(20);
        descriptor.LabeledEntries["room=bedroom"].GaugeValue.Should().Be(99);
    }

    [Fact]
    public void Labeled_histogram_observe_counts_per_label_value() {
        HistogramHandle handle = Obs.Metrics.RegisterHistogram("latency");

        Obs.Metrics.Observe(handle, 100, "route", "/api/v1");
        Obs.Metrics.Observe(handle, 200, "route", "/api/v1");
        Obs.Metrics.Observe(handle, 300, "route", "/api/v2");

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.LabeledEntries["route=/api/v1"].HistogramObservationCount.Should().Be(2);
        descriptor.LabeledEntries["route=/api/v2"].HistogramObservationCount.Should().Be(1);
    }

    [Fact]
    public void Canonicalize_empty_label_value_renders_key_equals_empty() {
        CardinalityGuard.Canonicalize("k", "").Should().Be("k=");
        CardinalityGuard.Canonicalize("k", null).Should().Be("k=");
    }

    [Fact]
    public void Canonicalize_throws_when_label_key_empty() {
        Action act = () => CardinalityGuard.Canonicalize("", "v");

        act.Should().Throw<ArgumentException>().WithMessage("Label key must not be empty.*");
    }

    [Fact]
    public void Canonicalize_params_throws_on_empty_label_set() {
        Action act = () => CardinalityGuard.Canonicalize(System.Array.Empty<(string, string)>());

        act.Should().Throw<ArgumentException>()
            .WithMessage("At least one label is required.*")
            .And.ParamName.Should().Be("labels");
    }

    [Fact]
    public void Canonicalize_params_throws_on_null_label_set() {
        Action act = () => CardinalityGuard.Canonicalize(null!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("At least one label is required.*")
            .And.ParamName.Should().Be("labels");
    }

    [Fact]
    public void Custom_cardinality_limit_on_registration_is_honored() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("strict", cardinalityLimit: 1);

        Obs.Metrics.Add(handle, 1, "k", "a");
        Obs.Metrics.Add(handle, 1, "k", "b");

        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.CardinalityLimit.Should().Be(1);
        descriptor.LabeledEntries.ContainsKey("k=a").Should().BeTrue();
        descriptor.LabeledEntries.ContainsKey(MetricDescriptor.OverflowLabel).Should().BeTrue();
        descriptor.CardinalityIncidentCount.Should().Be(1);
    }
}
