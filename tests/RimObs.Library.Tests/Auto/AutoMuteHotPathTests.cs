using System;
using FluentAssertions;
using RimWorks.RimObs.Profile;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Auto;

public sealed class AutoMuteHotPathTests : IDisposable {
    public void Dispose() {
        AutoMute.ResetForTests();
        Profiler.SetSink(null);
    }

    [Fact]
    public void Armed_self_time_fold_allocates_nothing_in_the_steady_state() {
        SectionHandle outer = SectionRegistry.Register("automute-alloc-outer");
        SectionHandle inner = SectionRegistry.Register("automute-alloc-inner");
        Profiler.SetSink(null);
        AutoMute.Arm();
        AutoMute.Watch(outer.Id);
        AutoMute.Watch(inner.Id);

        Nest(outer, inner, 50_000);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        Nest(outer, inner, 100_000);
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0);
    }

    // a thin wrapper around expensive work must read as cheap. without the fold the parent
    // inherits the child's whole duration and can never be judged trivial.
    [Fact]
    public void A_wrapper_is_not_charged_for_the_work_it_wraps() {
        SectionRegistry.Clear();
        AutoMute.ResetForTests();
        SectionHandle wrapper = SectionRegistry.Register("automute-self-wrapper");
        SectionHandle work = SectionRegistry.Register("automute-self-work");
        Profiler.SetSink(null);
        Profiler.Enabled = true;
        AutoMute.Arm();
        AutoMute.Watch(wrapper.Id);
        AutoMute.Watch(work.Id);

        for (int i = 0; i < AutoMute.SampleCount - 1; i++) {
            long a = Profiler.Start(wrapper);
            long b = Profiler.Start(work);
            Spin();
            Profiler.Stop(work, b);
            Profiler.Stop(wrapper, a);
        }

        long wrapperSelf = AutoMute.SelfTicksOf(wrapper.Id);
        long workSelf = AutoMute.SelfTicksOf(work.Id);
        wrapperSelf.Should().BeGreaterThan(0);
        (wrapperSelf * 3).Should().BeLessThan(workSelf);
    }

    private static void Spin() {
        long until = System.Diagnostics.Stopwatch.GetTimestamp()
            + System.Diagnostics.Stopwatch.Frequency / 100_000L;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until) {
        }
    }

    private static void Nest(SectionHandle outer, SectionHandle inner, int times) {
        for (int i = 0; i < times; i++) {
            long a = Profiler.Start(outer);
            long b = Profiler.Start(inner);
            Profiler.Stop(inner, b);
            Profiler.Stop(outer, a);
        }
    }
}
