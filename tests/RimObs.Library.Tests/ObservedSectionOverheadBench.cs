using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RimWorks.RimObs.Tests;

public sealed class ObservedSectionOverheadBench : IDisposable {
    private readonly ITestOutputHelper _out;

    public ObservedSectionOverheadBench(ITestOutputHelper output) {
        _out = output;
        TestBackend.Activate();
        PatchInstaller.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        OwnerRegistry.Clear();
        OwnerRegistry.RegisterMod(typeof(ObservedSectionOverheadBench).Assembly, "test.bench");
        ObservedSectionScanner.AttributesEnabledForTests = true;
    }

    public void Dispose() {
        ObservedSectionScanner.AttributesEnabledForTests = null;
        PatchInstaller.ResetForTests();
        TestBackend.Deactivate();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        OwnerRegistry.Clear();
    }

    private static int InstallAndGetSectionId() {
        IReadOnlyList<Assembly> asms = [typeof(BenchTarget).Assembly];
        ObservedSectionScanner.Scan([("test.bench", asms)]);

        string expectedName = $"test.bench.{typeof(BenchTarget).FullName}.Tick";
        CatalogEntry entry = SectionCatalog.Entries.First(e => e.Name == expectedName);
        entry.Installed.Should().BeTrue("PatchAttributeMethod must have run during scan");
        return entry.SectionId;
    }

    [Fact]
    public void DisabledSection_AllocatesZeroBytes() {
        int sectionId = InstallAndGetSectionId();
        SectionRegistry.SetActive(sectionId, false);
        Profiler.SetSink(null);

        // Warmup - let JIT compile the patched path
        for (int warm = 0; warm < 50_000; warm++)
            BenchTarget.Tick();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
            BenchTarget.Tick();
        long after = GC.GetAllocatedBytesForCurrentThread();

        long delta = after - before;
        _out.WriteLine($"disabled 100k BenchTarget.Tick alloc delta = {delta} bytes");
        delta.Should().Be(0, "disabled sections must allocate zero bytes on the hot path");
    }

    [Fact]
    public void EnabledSection_OverheadIsBounded() {
        int sectionId = InstallAndGetSectionId();
        SectionRegistry.SetActive(sectionId, true);
        Profiler.SetSink(null);

        // Warmup
        for (int warm = 0; warm < 50_000; warm++)
            BenchTarget.Tick();

        const int iterations = 100_000;
        const int trials = 5;

        double bestNsPerCall = double.MaxValue;
        for (int trial = 0; trial < trials; trial++) {
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                BenchTarget.Tick();
            sw.Stop();

            double ns = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
            if (ns < bestNsPerCall)
                bestNsPerCall = ns;
        }

        _out.WriteLine($"enabled BenchTarget.Tick best-of-{trials} = {bestNsPerCall:F1} ns/call ({iterations:N0} iterations/trial)");

        // PRD §11.6 target is < 1us. assert 10x (10us) to avoid flakes on virtualized runners; a
        // real regression blows past it.
        bestNsPerCall.Should().BeLessThan(10_000.0,
            $"enabled section overhead too high: {bestNsPerCall:F1}ns/call");
    }

    // the disabled branch is a load plus a conditional, so the honest number is the delta
    // against an identical uninstrumented method, not the patched method's own wall time.
    [Fact]
    public void DisabledSection_OverheadOverPlainCall() {
        int sectionId = InstallAndGetSectionId();
        SectionRegistry.SetActive(sectionId, false);
        Profiler.SetSink(null);

        double patched = BestNsPerCall(static () => BenchTarget.Tick());
        double plain = BestNsPerCall(static () => PlainTarget.Tick());
        double delta = patched - plain;

        _out.WriteLine($"disabled section: patched {patched:F2} ns/call, plain {plain:F2} ns/call, delta {delta:F2} ns");

        // budget is 5ns per .claude/rules/hot-path.md. assert 20x so a virtualized runner
        // cannot flake the build; the printed delta is what the gate decision reads.
        delta.Should().BeLessThan(100.0, $"disabled section overhead too high: {delta:F2}ns/call");
    }

    private static double BestNsPerCall(Action work) {
        const int iterations = 200_000;
        const int trials = 7;

        for (int warm = 0; warm < 50_000; warm++)
            work();

        double best = double.MaxValue;
        for (int trial = 0; trial < trials; trial++) {
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                work();
            sw.Stop();

            double ns = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
            if (ns < best)
                best = ns;
        }
        return best;
    }

    public static class BenchTarget {
        [ObservedSection]
        public static int Tick() {
            int x = 0;
            for (int i = 0; i < 4; i++)
                x += i;
            return x;
        }
    }

    // same body as BenchTarget.Tick with no attribute, so the delta isolates the patch.
    public static class PlainTarget {
        public static int Tick() {
            int x = 0;
            for (int i = 0; i < 4; i++)
                x += i;
            return x;
        }
    }
}
