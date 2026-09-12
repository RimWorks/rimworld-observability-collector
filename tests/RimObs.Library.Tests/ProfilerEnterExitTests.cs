using System;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

// the concord backend injects EnterById at head and ExitById in a finally; the pair carries
// the start token in thread state because injections cannot share an IL local.
public sealed class ProfilerEnterExitTests : IDisposable {
    private sealed class CapturingSink : ISampleSink {
        public readonly List<(int SectionId, int ParentId, long Elapsed)> Samples = [];

        public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
            Samples.Add((sectionId, parentId, elapsedTicks));
        }
    }

    private readonly CapturingSink _sink = new();
    private readonly SectionHandle _outer;
    private readonly SectionHandle _inner;

    public ProfilerEnterExitTests() {
        SectionRegistry.Clear();
        AutoMute.ResetForTests();
        _outer = SectionRegistry.Register("test.enterexit_outer");
        _inner = SectionRegistry.Register("test.enterexit_inner");
        Profiler.SetSink(_sink);
        Profiler.Enabled = true;
    }

    public void Dispose() {
        Profiler.SetSink(null);
        AutoMute.ResetForTests();
        SectionRegistry.Clear();
    }

    [Fact]
    public void A_balanced_pair_records_one_sample() {
        Profiler.EnterById(_outer.Id);
        Profiler.ExitById(_outer.Id);

        _sink.Samples.Should().ContainSingle();
        _sink.Samples[0].SectionId.Should().Be(_outer.Id);
        _sink.Samples[0].Elapsed.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Nesting_reports_the_parent_section() {
        Profiler.EnterById(_outer.Id);
        Profiler.EnterById(_inner.Id);
        Profiler.ExitById(_inner.Id);
        Profiler.ExitById(_outer.Id);

        _sink.Samples.Should().HaveCount(2);
        _sink.Samples[0].SectionId.Should().Be(_inner.Id);
        _sink.Samples[0].ParentId.Should().Be(_outer.Id);
        _sink.Samples[1].ParentId.Should().Be(Profiler.NoParent);
    }

    [Fact]
    public void A_disabled_section_pops_clean_and_records_nothing() {
        SectionRegistry.SetActive(_inner.Id, false);

        Profiler.EnterById(_outer.Id);
        Profiler.EnterById(_inner.Id);
        Profiler.ExitById(_inner.Id);
        Profiler.ExitById(_outer.Id);

        _sink.Samples.Should().ContainSingle();
        _sink.Samples[0].SectionId.Should().Be(_outer.Id);
    }

    // a method entered before its patch landed runs Exit without a matching Enter; the top
    // frame belongs to someone else and must survive.
    [Fact]
    public void An_exit_without_its_enter_leaves_the_parent_frame_alone() {
        Profiler.EnterById(_outer.Id);
        Profiler.ExitById(_inner.Id);
        Profiler.ExitById(_outer.Id);

        _sink.Samples.Should().ContainSingle();
        _sink.Samples[0].SectionId.Should().Be(_outer.Id);
    }

    [Fact]
    public void Enter_exit_interleaves_with_the_token_pair() {
        Profiler.EnterById(_outer.Id);
        long token = Profiler.StartById(_inner.Id);
        Profiler.StopById(_inner.Id, token);
        Profiler.ExitById(_outer.Id);

        _sink.Samples.Should().HaveCount(2);
        _sink.Samples[0].SectionId.Should().Be(_inner.Id);
        _sink.Samples[0].ParentId.Should().Be(_outer.Id);
    }

    [Fact]
    public void Past_the_hard_depth_cap_the_pair_stays_balanced() {
        for (int i = 0; i < Profiler.MaxStackDepthForTests + 8; i++)
            Profiler.EnterById(_outer.Id);
        for (int i = 0; i < Profiler.MaxStackDepthForTests + 8; i++)
            Profiler.ExitById(_outer.Id);

        Profiler.EnterById(_inner.Id);
        Profiler.ExitById(_inner.Id);

        _sink.Samples.Should().Contain(s => s.SectionId == _inner.Id && s.ParentId == Profiler.NoParent);
    }

    private sealed class CountingSink : ISampleSink {
        public long Count;

        public void RecordSection(int sectionId, int parentId, int nodeId, int parentNodeId, long startTimestamp, long elapsedTicks, long allocBytes) {
            Count++;
        }
    }

    [Fact]
    public void A_pinned_stack_heals_at_the_frame_boundary_and_records_again() {
        for (int i = 0; i < Profiler.MaxStackDepthForTests; i++)
            Profiler.EnterById(_outer.Id);
        _sink.Samples.Clear();

        Profiler.HealPinnedAtFrameBoundary();

        Profiler.EnterById(_inner.Id);
        Profiler.ExitById(_inner.Id);
        _sink.Samples.Should().ContainSingle(s => s.SectionId == _inner.Id);
    }

    [Fact]
    public void A_healthy_stack_is_left_alone_by_the_heal() {
        Profiler.EnterById(_outer.Id);

        Profiler.HealPinnedAtFrameBoundary();

        Profiler.EnterById(_inner.Id);
        Profiler.ExitById(_inner.Id);
        Profiler.ExitById(_outer.Id);
        _sink.Samples.Should().HaveCount(2);
        _sink.Samples[0].ParentId.Should().Be(_outer.Id);
    }

    [Fact]
    public void The_pair_allocates_nothing_in_steady_state() {
        CountingSink counting = new();
        Profiler.SetSink(counting);
        for (int warm = 0; warm < 50_000; warm++) {
            Profiler.EnterById(_outer.Id);
            Profiler.ExitById(_outer.Id);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) {
            Profiler.EnterById(_outer.Id);
            Profiler.ExitById(_outer.Id);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).Should().Be(0);
        counting.Count.Should().Be(150_000);
    }
}
