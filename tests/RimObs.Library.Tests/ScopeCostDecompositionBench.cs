using System;
using System.Diagnostics;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RimWorks.RimObs.Tests;

// each stage adds one ingredient, so the delta between lines prices that ingredient.
// CoreCLR numbers; Mono pays more for [ThreadStatic] and GetTimestamp, ratios travel.
public sealed class ScopeCostDecompositionBench : IDisposable {
    private readonly ITestOutputHelper _out;

    public ScopeCostDecompositionBench(ITestOutputHelper output) {
        _out = output;
        SectionRegistry.Clear();
        AutoMute.ResetForTests();
        Profiler.SetSink(null);
    }

    public void Dispose() {
        Profiler.SetSink(null);
        AutoMute.ResetForTests();
        SectionRegistry.Clear();
    }

    private sealed class NoopSink : ISampleSink {
        public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
        }
    }

    private sealed class RingSink : ISampleSink {
        public readonly SampleRingBuffer Ring = new(1 << 16);

        public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
            Ring.TryWrite(sectionId, parentId, nodeId, parentNodeId, startTimestamp, elapsedTicks, 0, allocBytes);
        }
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Decompose_one_enabled_scope() {
        SectionHandle on = SectionRegistry.Register("decomp-on");
        SectionHandle off = SectionRegistry.Register("decomp-off");
        SectionRegistry.SetActive(off.Id, false);

        double timestamps = Measure(static () => {
            long t = Stopwatch.GetTimestamp();
            Sink64(Stopwatch.GetTimestamp() - t);
        });
        double disabled = Measure(() => {
            long t = Profiler.Start(off);
            Profiler.Stop(off, t);
        });

        double bareNoSink = Measure(() => {
            long t = Profiler.Start(on);
            Profiler.Stop(on, t);
        });

        NoopSink noop = new();
        Profiler.SetSink(noop);
        double noopSink = Measure(() => {
            long t = Profiler.Start(on);
            Profiler.Stop(on, t);
        });

        RingSink ring = new();
        SampleBatch batch = new(4096);
        Profiler.SetSink(ring);
        double ringSink = Measure(() => {
            long t = Profiler.Start(on);
            Profiler.Stop(on, t);
        }, everyChunk: () => ring.Ring.Drain(batch, 4096));

        Profiler.SetSink(noop);
        AutoMute.Arm();
        AutoMute.Watch(on.Id);
        // burn past the one-shot judgment with real elapsed time so it never mutes.
        for (int i = 0; i < AutoMute.SampleCount + 8; i++)
            AutoMute.Observe(on.Id, AutoMute.BudgetTicks + 1);
        double armed = Measure(() => {
            long t = Profiler.Start(on);
            Profiler.Stop(on, t);
        });
        SectionRegistry.IsActive(on.Id).Should().BeTrue("the bench section must never be muted mid-measure");

        _out.WriteLine($"two GetTimestamp calls        {timestamps,7:F2} ns");
        _out.WriteLine($"disabled pair (branch floor)  {disabled,7:F2} ns");
        _out.WriteLine($"enabled, no sink, unarmed     {bareNoSink,7:F2} ns  (bookkeeping = {bareNoSink - timestamps:F2})");
        _out.WriteLine($"enabled, noop sink            {noopSink,7:F2} ns  (dispatch = {noopSink - bareNoSink:F2})");
        _out.WriteLine($"enabled, ring sink            {ringSink,7:F2} ns  (ring write = {ringSink - noopSink:F2})");
        _out.WriteLine($"enabled, noop sink, armed     {armed,7:F2} ns  (automute fold = {armed - noopSink:F2})");
        _out.WriteLine($"blackhole {s_Blackhole}");

        ringSink.Should().BeLessThan(2000.0);
    }

    private static long s_Blackhole;

    private static void Sink64(long v) => s_Blackhole ^= v;

    private static double Measure(Action op, Action? everyChunk = null) {
        const int iterations = 2_000_000;
        const int chunk = 4096;
        const int trials = 5;

        for (int warm = 0; warm < 50_000; warm++)
            op();
        everyChunk?.Invoke();

        double best = double.MaxValue;
        for (int trial = 0; trial < trials; trial++) {
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) {
                op();
                if (everyChunk != null && (i & (chunk - 1)) == chunk - 1)
                    everyChunk();
            }
            sw.Stop();
            double ns = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
            if (ns < best)
                best = ns;
        }

        return best;
    }
}
