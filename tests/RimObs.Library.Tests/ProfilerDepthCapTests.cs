using System.Collections.Generic;
using FluentAssertions;
using RimWorks.RimObs.Profile;
using Xunit;

namespace RimWorks.RimObs.Tests;

/// <summary>
/// Guards the max-capture-depth cap. An uncapped tree floods the ring once auto-instrumentation
/// is on, and a cap that forgets to balance start/stop corrupts every parent link below it.
/// </summary>
public sealed class ProfilerDepthCapTests : IDisposable {
    private sealed class CapturingSink : ISampleSink {
        public readonly List<(int SectionId, int ParentId)> Records = new();

        public void RecordSection(
            int sectionId, int parentId, int nodeId, int parentNodeId,
            long startTimestamp, long elapsedTicks, long allocBytes
        ) => Records.Add((sectionId, parentId));
    }

    private readonly CapturingSink _sink = new();
    private readonly int _previousDepth = Profiler.MaxDepth;

    public ProfilerDepthCapTests() {
        SectionRegistry.Clear();
        Profiler.SetSink(_sink);
        Profiler.Enabled = true;
    }

    public void Dispose() {
        Profiler.SetSink(null);
        Profiler.MaxDepth = _previousDepth;
        SectionRegistry.Clear();
    }

    private static SectionHandle[] Register(int count) {
        SectionHandle[] handles = new SectionHandle[count];
        for (int i = 0; i < count; i++) {
            handles[i] = SectionRegistry.Register($"depth-cap-{i}");
            SectionRegistry.SetActive(handles[i].Id, true);
        }
        return handles;
    }

    [Fact]
    public void Default_cap_is_eight() {
        Profiler.MaxDepth.Should().Be(8);
    }

    [Fact]
    public void Sections_at_or_below_the_cap_record_and_deeper_ones_do_not() {
        Profiler.MaxDepth = 3;
        SectionHandle[] handles = Register(6);

        long[] tokens = new long[6];
        for (int i = 0; i < 6; i++)
            tokens[i] = Profiler.Start(handles[i]);
        for (int i = 5; i >= 0; i--)
            Profiler.Stop(handles[i], tokens[i]);

        _sink.Records.Should().HaveCount(3);
        _sink.Records[0].SectionId.Should().Be(handles[2].Id);
        _sink.Records[2].SectionId.Should().Be(handles[0].Id);
    }

    [Fact]
    public void Depth_returns_to_zero_after_an_over_cap_run_so_the_next_tree_is_not_shifted() {
        Profiler.MaxDepth = 2;
        SectionHandle[] deep = Register(5);

        long[] tokens = new long[5];
        for (int i = 0; i < 5; i++)
            tokens[i] = Profiler.Start(deep[i]);
        for (int i = 4; i >= 0; i--)
            Profiler.Stop(deep[i], tokens[i]);

        _sink.Records.Clear();

        SectionHandle top = SectionRegistry.Register("depth-cap-after");
        SectionRegistry.SetActive(top.Id, true);
        long token = Profiler.Start(top);
        Profiler.Stop(top, token);

        _sink.Records.Should().ContainSingle();
        _sink.Records[0].Should().Be((top.Id, Profiler.NoParent));
    }

    [Fact]
    public void Cap_is_clamped_into_the_usable_stack_range() {
        Profiler.MaxDepth = 0;
        Profiler.MaxDepth.Should().Be(1);

        Profiler.MaxDepth = 9999;
        Profiler.MaxDepth.Should().Be(Profiler.MaxStackDepth);
    }
}
