using RimWorks.RimObs.Collector.Exporters;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class PrometheusExpositionTests {
    [Fact]
    public void WriteMetadata_emits_help_and_type_lines_once() {
        PrometheusExposition ex = new();
        ex.WriteMetadata("rimobs_tps", "gauge", "Ticks per second.");
        ex.WriteMetadata("rimobs_tps", "gauge", "Duplicate ignored.");
        ex.WriteSample("rimobs_tps", 60);

        string text = ex.ToString();
        text.Should().Contain("# HELP rimobs_tps Ticks per second.\n");
        text.Should().Contain("# TYPE rimobs_tps gauge\n");
        text.Split("# TYPE rimobs_tps").Length.Should().Be(2, "metadata is declared only once per metric");
        text.Should().Contain("rimobs_tps 60\n");
    }

    [Fact]
    public void WriteSample_with_labels_renders_canonical_label_block() {
        PrometheusExposition ex = new();
        ex.WriteSample("rimobs_gc_collections_total", [new PrometheusLabel("generation", "2")], 5);

        ex.ToString().Should().Contain("rimobs_gc_collections_total{generation=\"2\"} 5\n");
    }

    [Fact]
    public void EscapeLabelValue_escapes_backslash_quote_and_newline() {
        PrometheusExposition.EscapeLabelValue("a\\b\"c\nd").Should().Be("a\\\\b\\\"c\\nd");
    }

    [Fact]
    public void EscapeHelp_escapes_backslash_and_newline_but_not_quotes() {
        PrometheusExposition.EscapeHelp("a\\b\nc\"d").Should().Be("a\\\\b\\nc\"d");
    }

    [Fact]
    public void FormatValue_handles_special_doubles() {
        PrometheusExposition.FormatValue(double.NaN).Should().Be("NaN");
        PrometheusExposition.FormatValue(double.PositiveInfinity).Should().Be("+Inf");
        PrometheusExposition.FormatValue(double.NegativeInfinity).Should().Be("-Inf");
    }
}
