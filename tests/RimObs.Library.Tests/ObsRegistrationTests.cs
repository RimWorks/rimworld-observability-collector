using System;
using System.Reflection;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Metrics;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class ObsRegistrationTests : IDisposable {
    private const string TestPackageId = "RimWorks.RimObs.tests";
    private readonly Assembly _self;

    public ObsRegistrationTests() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
        _self = typeof(ObsRegistrationTests).Assembly;
        OwnerRegistry.RegisterMod(_self, TestPackageId);
    }

    public void Dispose() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
    }

    [Fact]
    public void RegisterSection_auto_prefixes_packageId() {
        SectionHandle handle = Obs.Profile.RegisterSection("pawn_scan");

        handle.IsValid.Should().BeTrue();
        SectionRegistry.GetName(handle.Id).Should().Be("RimWorks.RimObs.tests.pawn_scan");
    }

    [Fact]
    public void RegisterCounter_auto_prefixes_and_returns_handle() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("cache_hits", subsystem: "jobs", unit: "count");

        handle.IsValid.Should().BeTrue();
        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.FullName.Should().Be("RimWorks.RimObs.tests.cache_hits");
        descriptor.OwnerPackageId.Should().Be(TestPackageId);
        descriptor.Kind.Should().Be(MetricKind.Counter);
        descriptor.Subsystem.Should().Be("jobs");
        descriptor.Unit.Should().Be("count");
    }

    [Fact]
    public void RegisterGauge_returns_gauge_handle() {
        GaugeHandle handle = Obs.Metrics.RegisterGauge("pending_jobs");

        handle.IsValid.Should().BeTrue();
        MetricRegistry.Get(handle.Id)!.Kind.Should().Be(MetricKind.Gauge);
    }

    [Fact]
    public void RegisterHistogram_returns_histogram_handle() {
        HistogramHandle handle = Obs.Metrics.RegisterHistogram("search_duration_us", unit: "us");

        handle.IsValid.Should().BeTrue();
        MetricDescriptor descriptor = MetricRegistry.Get(handle.Id)!;
        descriptor.Kind.Should().Be(MetricKind.Histogram);
        descriptor.Unit.Should().Be("us");
    }

    [Fact]
    public void Registration_throws_when_assembly_unregistered() {
        OwnerRegistry.Clear();

        Action act = () => Obs.Profile.RegisterSection("foo");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not registered with RimObs*");
    }

    [Fact]
    public void Re_registering_same_name_returns_same_descriptor() {
        CounterHandle a = Obs.Metrics.RegisterCounter("counter1");
        CounterHandle b = Obs.Metrics.RegisterCounter("counter1");

        a.Id.Should().Be(b.Id);
    }

    [Fact]
    public void Re_registering_same_name_with_different_kind_throws() {
        Obs.Metrics.RegisterCounter("conflict");

        Action act = () => Obs.Metrics.RegisterGauge("conflict");

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered as Counter*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Foo")]
    [InlineData("1foo")]
    [InlineData("foo-bar")]
    [InlineData("foo.bar")]
    [InlineData("foo bar")]
    public void RegisterSection_rejects_invalid_bare_names(string bad) {
        Action act = () => Obs.Profile.RegisterSection(bad);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("foo_bar")]
    [InlineData("foo123")]
    [InlineData("a")]
    public void RegisterSection_accepts_valid_bare_names(string good) {
        Action act = () => Obs.Profile.RegisterSection(good);

        act.Should().NotThrow();
    }

    [Fact]
    public void Counter_add_increments_total() {
        CounterHandle handle = Obs.Metrics.RegisterCounter("hits");

        Obs.Metrics.Add(handle, 5);
        Obs.Metrics.Add(handle, 3);

        MetricRegistry.Get(handle.Id)!.CounterTotal.Should().Be(8);
    }

    [Fact]
    public void Gauge_set_replaces_value() {
        GaugeHandle handle = Obs.Metrics.RegisterGauge("level");

        Obs.Metrics.Set(handle, 10);
        Obs.Metrics.Set(handle, 42);

        MetricRegistry.Get(handle.Id)!.GaugeValue.Should().Be(42);
    }

    [Fact]
    public void Histogram_observe_increments_observation_count() {
        HistogramHandle handle = Obs.Metrics.RegisterHistogram("durations");

        Obs.Metrics.Observe(handle, 100);
        Obs.Metrics.Observe(handle, 200);
        Obs.Metrics.Observe(handle, 300);

        MetricRegistry.Get(handle.Id)!.HistogramObservationCount.Should().Be(3);
    }

    [Fact]
    public void Measure_scope_dispose_records_sample_for_active_section() {
        RecordingSink sink = new();
        Profiler.SetSink(sink);
        Profiler.Enabled = true;
        try {
            SectionHandle handle = Obs.Profile.RegisterSection("scoped_op");
            SectionRegistry.SetActive(handle.Id, true);

            using (Obs.Profile.Measure(handle)) {
            }

            sink.Count.Should().Be(1);
        }
        finally {
            Profiler.SetSink(null);
        }
    }


    [Fact]
    public void Obs_RegisterSection_PropagatesSubsystem() {
        SectionRegistry.Clear();
        OwnerRegistry.RegisterMod(typeof(ObsRegistrationTests).Assembly, "test.modid");

        SectionHandle handle = Obs.Profile.RegisterSection("foo", subsystem: "jobs");

        SectionRegistry.GetSubsystem(handle.Id).Should().Be("jobs");
    }

    private sealed class RecordingSink : ISampleSink {
        public int Count;

        public void RecordSection(int sectionId, int parentId, long startTimestamp, long elapsedTicks) {
            Count++;
        }
    }
}
