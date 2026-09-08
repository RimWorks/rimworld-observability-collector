using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

/// <summary>
/// The stack arithmetic, driven through AllocationHook.Accumulate. These run on net10, where
/// the mono allocation callback does not exist, so the counter is fed by hand instead.
/// </summary>
public sealed class ProfilerAllocTests {
    [Fact]
    public void Scope_reports_the_bytes_allocated_inside_it() {
        SectionHandle section = SectionRegistry.Register("alloc-single");
        SectionRegistry.SetActive(section.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long token = Profiler.Start(section);
            AllocationHook.Accumulate(96);
            Profiler.Stop(section, token);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().ContainSingle();
        sink.Samples[0].AllocBytes.Should().Be(96);
    }

    [Fact]
    public void Bytes_allocated_before_the_scope_opened_are_not_counted() {
        SectionHandle section = SectionRegistry.Register("alloc-before");
        SectionRegistry.SetActive(section.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            AllocationHook.Accumulate(1024);
            long token = Profiler.Start(section);
            Profiler.Stop(section, token);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples[0].AllocBytes.Should().Be(0);
    }

    [Fact]
    public void Parent_total_includes_the_child_scope_the_way_elapsed_does() {
        SectionHandle parent = SectionRegistry.Register("alloc-parent");
        SectionHandle child = SectionRegistry.Register("alloc-child");
        SectionRegistry.SetActive(parent.Id, true);
        SectionRegistry.SetActive(child.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long outer = Profiler.Start(parent);
            AllocationHook.Accumulate(10);
            long inner = Profiler.Start(child);
            AllocationHook.Accumulate(32);
            Profiler.Stop(child, inner);
            AllocationHook.Accumulate(8);
            Profiler.Stop(parent, outer);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().HaveCount(2);
        sink.Samples[0].AllocBytes.Should().Be(32);
        sink.Samples[1].AllocBytes.Should().Be(50);
    }

    [Fact]
    public void A_disabled_section_records_nothing() {
        SectionHandle section = SectionRegistry.Register("alloc-disabled");
        SectionRegistry.SetActive(section.Id, false);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long token = Profiler.Start(section);
            AllocationHook.Accumulate(4096);
            Profiler.Stop(section, token);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().BeEmpty();
    }

    [Fact]
    public void Accumulate_tracks_object_count_alongside_bytes() {
        long bytes = AllocationHook.t_Bytes;
        long count = AllocationHook.t_Count;

        AllocationHook.Accumulate(16);
        AllocationHook.Accumulate(24);

        (AllocationHook.t_Bytes - bytes).Should().Be(40);
        (AllocationHook.t_Count - count).Should().Be(2);
    }
}

public sealed class AllocationHookAvailabilityTests {
    [Fact]
    public void TryEnable_is_terminal_and_never_retries() {
        // the outcome depends on whether a mono boehm runtime is on the box, so assert the
        // contract rather than the answer: one attempt, one verdict, no per-call retry.
        bool first = AllocationHook.TryEnable();
        int state = AllocationHook.State;

        state.Should().NotBe(AllocationHook.Off);
        AllocationHook.IsOn.Should().Be(first);
        AllocationHook.TryEnable().Should().Be(first);
        AllocationHook.State.Should().Be(state);
    }
}
