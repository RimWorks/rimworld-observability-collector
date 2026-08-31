using System.Diagnostics;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Exporters;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class PrometheusMetricsBuilderTests {
    private static SessionAggregator Populated() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta {
            SessionId = "s1",
            StopwatchFrequency = Stopwatch.Frequency,
        });
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [1],
            Names = ["mymod.tick"],
        });
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [1, 1],
            StartTimestamps = [10, 20],
            ElapsedTicks = [100, 300],
        });
        agg.OnGcEvents(new GcEventsBatch {
            Generations = [0, 0, 1],
            PauseTypes = [0, 0, 0],
            HeapBefore = [10, 10, 20],
            HeapAfter = [5, 5, 15],
            DurationMicros = [1000, 2000, 3000],
            Ticks = [1, 2, 3],
            AllocationRateBytesPerMinute = [0, 0, 0],
        });
        agg.OnTpsFps(new TpsFpsBatch { Tps = 60, Fps = 144, Tick = 5 });
        return agg;
    }

    [Fact]
    public void Render_emits_receive_section_gc_and_tps_metrics() {
        PrometheusRender render = new PrometheusMetricsBuilder().Render(Populated());
        string text = render.Body;

        text.Should().Contain("# TYPE rimobs_collector_connected gauge");
        text.Should().Contain("rimobs_collector_connected 1");
        text.Should().Contain("rimobs_collector_batches_total");
        text.Should().Contain("rimobs_tps 60");
        text.Should().Contain("rimobs_fps 144");
        text.Should().Contain("rimobs_section_duration_seconds_count{section=\"mymod.tick\"} 2");
        text.Should().Contain("rimobs_gc_collections_total{generation=\"0\"} 2");
        text.Should().Contain("rimobs_gc_collections_total{generation=\"1\"} 1");
        text.Should().Contain("rimobs_managed_heap_bytes{generation=\"1\"} 15");
        render.SampleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Render_with_empty_aggregator_omits_session_dependent_metrics() {
        PrometheusRender render = new PrometheusMetricsBuilder().Render(new SessionAggregator());
        string text = render.Body;

        text.Should().Contain("rimobs_collector_connected 0");
        text.Should().NotContain("rimobs_tps ");
        text.Should().NotContain("rimobs_gc_collections_total");
    }

    [Fact]
    public void Render_collapses_section_label_overflow_to_other() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = "s", StopwatchFrequency = Stopwatch.Frequency });
        for (int i = 0; i < 5; i++) {
            agg.OnSectionRegistrations(new SectionRegistrationsBatch { SectionIds = [i], Names = [$"sec{i}"] });
            agg.OnSectionBatch(new SectionBatch {
                SectionIds = [i],
                StartTimestamps = [0],
                ElapsedTicks = [10],
            });
        }

        string text = new PrometheusMetricsBuilder(labelCardinalityLimit: 2).Render(agg).Body;
        text.Should().Contain($"section=\"{PrometheusLabelCardinality.OverflowValue}\"");
    }

    [Fact]
    public void Sanitize_replaces_invalid_metric_name_characters() {
        PrometheusMetricsBuilder.Sanitize("my.mod-metric!").Should().Be("my_mod_metric_");
    }
}
