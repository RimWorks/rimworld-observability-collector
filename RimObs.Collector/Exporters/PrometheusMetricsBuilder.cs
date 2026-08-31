using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Exporters;

// Renders the collector's session aggregates as a Prometheus text exposition.
// Reads only from summarized aggregates (never a game hot path) per PRD §17.1,
// and caps label cardinality per dimension per PRD §17.5. Durations are emitted
// in seconds following Prometheus naming conventions (PRD §11 timing notes).
public sealed class PrometheusMetricsBuilder {
    public const int DefaultLabelCardinalityLimit = 64;

    private const string Ns = "rimobs_";
    private const int TopSectionLimit = 64;
    private const string GaugeType = "gauge";
    private const string CounterType = "counter";

    private readonly int _labelCardinalityLimit;

    public PrometheusMetricsBuilder() : this(DefaultLabelCardinalityLimit) {
    }

    public PrometheusMetricsBuilder(int labelCardinalityLimit) {
        _labelCardinalityLimit = labelCardinalityLimit < 1 ? 1 : labelCardinalityLimit;
    }

    public PrometheusRender Render(SessionAggregator aggregator) {
        PrometheusExposition exposition = new();
        int sampleCount = 0;

        sampleCount += WriteReceiveMetrics(exposition, aggregator);
        sampleCount += WriteTpsFps(exposition, aggregator);
        sampleCount += WriteSections(exposition, aggregator);
        sampleCount += WriteGc(exposition, aggregator);
        sampleCount += WriteCustomMetrics(exposition, aggregator);

        return new PrometheusRender(exposition.ToString(), sampleCount);
    }

    private static int WriteReceiveMetrics(PrometheusExposition ex, SessionAggregator aggregator) {
        ex.WriteMetadata(Ns + "collector_connected", GaugeType, "1 when a session is currently reporting, else 0.");
        ex.WriteSample(Ns + "collector_connected", aggregator.Meta is null ? 0 : 1);

        ex.WriteMetadata(Ns + "collector_batches_total", CounterType, "Total telemetry batches received by the collector.");
        ex.WriteSample(Ns + "collector_batches_total", aggregator.TotalBatches);

        ex.WriteMetadata(Ns + "collector_samples_total", CounterType, "Total section timing samples received.");
        ex.WriteSample(Ns + "collector_samples_total", aggregator.TotalSamples);

        return 3;
    }

    private static int WriteTpsFps(PrometheusExposition ex, SessionAggregator aggregator) {
        if (!aggregator.HasTpsFps)
            return 0;

        ex.WriteMetadata(Ns + "tps", GaugeType, "Most recent ticks-per-second sample.");
        ex.WriteSample(Ns + "tps", aggregator.LatestTps);

        ex.WriteMetadata(Ns + "fps", GaugeType, "Most recent frames-per-second sample.");
        ex.WriteSample(Ns + "fps", aggregator.LatestFps);

        return 2;
    }

    private int WriteSections(PrometheusExposition ex, SessionAggregator aggregator) {
        double nsPerTick = NsPerTick(aggregator.Meta);
        double secPerTick = nsPerTick / 1_000_000_000.0;

        List<SectionStats> sections = new(aggregator.Sections);
        sections.Sort(static (a, b) => b.TotalElapsedTicks.CompareTo(a.TotalElapsedTicks));

        bool metaWritten = false;
        PrometheusLabelCardinality sectionLabels = new(_labelCardinalityLimit);
        int samples = 0;
        int count = Math.Min(sections.Count, TopSectionLimit);

        for (int i = 0; i < count; i++) {
            SectionStats s = sections[i];
            if (!metaWritten) {
                ex.WriteMetadata(Ns + "section_duration_seconds_count", CounterType, "Section timing sample count, top sections only.");
                ex.WriteMetadata(Ns + "section_duration_seconds_sum", CounterType, "Section total elapsed time in seconds, top sections only.");
                ex.WriteMetadata(Ns + "section_duration_seconds_max", GaugeType, "Section maximum elapsed time in seconds, top sections only.");
                metaWritten = true;
            }

            string section = sectionLabels.Resolve(string.IsNullOrEmpty(s.Name) ? s.SectionId.ToString() : s.Name);
            PrometheusLabel[] labels = [new("section", section)];

            ex.WriteSample(Ns + "section_duration_seconds_count", labels, s.SampleCount);
            ex.WriteSample(Ns + "section_duration_seconds_sum", labels, s.TotalElapsedTicks * secPerTick);
            ex.WriteSample(Ns + "section_duration_seconds_max", labels, s.MaxElapsedTicks * secPerTick);
            samples += 3;
        }

        return samples;
    }

    private int WriteGc(PrometheusExposition ex, SessionAggregator aggregator) {
        GcEventRecord[] events = aggregator.SnapshotGcEvents(int.MaxValue);
        if (events.Length == 0)
            return 0;

        Dictionary<byte, GcGenerationAccumulator> byGen = new();
        for (int i = 0; i < events.Length; i++) {
            GcEventRecord e = events[i];
            if (!byGen.TryGetValue(e.Generation, out GcGenerationAccumulator acc))
                acc = new GcGenerationAccumulator();
            acc.Collections++;
            acc.PauseSecondsSum += e.DurationMicros / 1_000_000.0;
            acc.LatestHeapAfter = e.HeapAfter;
            byGen[e.Generation] = acc;
        }

        ex.WriteMetadata(Ns + "gc_collections_total", CounterType, "GC collections observed, by generation.");
        ex.WriteMetadata(Ns + "gc_pause_seconds_sum", CounterType, "Total GC pause time in seconds, by generation.");
        ex.WriteMetadata(Ns + "gc_pause_seconds_count", CounterType, "GC pause sample count, by generation.");
        ex.WriteMetadata(Ns + "managed_heap_bytes", GaugeType, "Most recent managed heap size in bytes, by generation.");

        int samples = 0;
        PrometheusLabelCardinality genLabels = new(_labelCardinalityLimit);
        foreach (KeyValuePair<byte, GcGenerationAccumulator> kv in byGen) {
            string gen = genLabels.Resolve(kv.Key.ToString());
            PrometheusLabel[] labels = [new("generation", gen)];
            ex.WriteSample(Ns + "gc_collections_total", labels, kv.Value.Collections);
            ex.WriteSample(Ns + "gc_pause_seconds_sum", labels, kv.Value.PauseSecondsSum);
            ex.WriteSample(Ns + "gc_pause_seconds_count", labels, kv.Value.Collections);
            ex.WriteSample(Ns + "managed_heap_bytes", labels, kv.Value.LatestHeapAfter);
            samples += 4;
        }

        return samples;
    }

    private int WriteCustomMetrics(PrometheusExposition ex, SessionAggregator aggregator) {
        int samples = 0;
        foreach (MetricStats metric in aggregator.Metrics) {
            if (string.IsNullOrEmpty(metric.Name))
                continue;

            string name = Ns + "metric_" + Sanitize(metric.Name);
            string type = metric.Kind switch {
                MetricKind.Counter => CounterType,
                MetricKind.Gauge => GaugeType,
                _ => "untyped",
            };
            ex.WriteMetadata(name, type, $"Custom {metric.Kind} '{metric.Name}'.");

            PrometheusLabelCardinality labelGuard = new(_labelCardinalityLimit);
            foreach (MetricLabelStats label in metric.Labels.Values) {
                string canonical = labelGuard.Resolve(label.Canonical);
                PrometheusLabel[] labels = canonical.Length == 0 ? [] : [new("label", canonical)];
                ex.WriteSample(name, labels, System.Threading.Interlocked.Read(ref label.LatestValue));
                samples++;
            }
        }
        return samples;
    }

    internal static string Sanitize(string name) {
        System.Text.StringBuilder sb = new(name.Length);
        for (int i = 0; i < name.Length; i++) {
            char c = name[i];
            bool valid = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or ':';
            sb.Append(valid ? c : '_');
        }
        return sb.ToString();
    }

    private static double NsPerTick(SessionMeta? meta) => TickConverter.NsPerTick(meta);

    private struct GcGenerationAccumulator {
        public long Collections;
        public double PauseSecondsSum;
        public long LatestHeapAfter;
    }
}
