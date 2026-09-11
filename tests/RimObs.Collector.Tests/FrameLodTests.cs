using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

// a wide range selection cannot show sub-pixel nodes, so the range endpoint serves a
// duration floor and drops what the view cannot draw. parents always outlast children,
// so chains stay intact.
public sealed class FrameLodTests {
    private static FrameSnapshot Frame() => new(
        CaptureOrdinal: 7,
        StartTicks: 0,
        EndTicks: 1000,
        SectionIds: [1, 2, 3],
        ParentIds: [-1, 1, 1],
        NodeIds: [10, 11, 12],
        ParentNodeIds: [-1, 10, 10],
        NodeStartTicks: [0L, 10L, 500L],
        NodeElapsedTicks: [1000L, 5L, 400L],
        NodeAllocBytes: [0L, 1L, 2L],
        ThreadIds: [1, 1, 7]);

    [Fact]
    public void Drops_nodes_shorter_than_the_floor_and_keeps_the_rest_aligned() {
        FrameSnapshot filtered = FrameLod.Filter(Frame(), minDurTicks: 100L);

        filtered.SectionIds.Should().Equal(1, 3);
        filtered.NodeIds.Should().Equal(10, 12);
        filtered.ParentNodeIds.Should().Equal(-1, 10);
        filtered.NodeElapsedTicks.Should().Equal(1000L, 400L);
        filtered.ThreadIds.Should().Equal(1, 7);
        filtered.CaptureOrdinal.Should().Be(7);
    }

    [Fact]
    public void A_zero_floor_returns_the_same_snapshot_instance() {
        FrameSnapshot frame = Frame();
        FrameLod.Filter(frame, 0L).Should().BeSameAs(frame);
    }

    [Fact]
    public void A_frame_with_no_thread_ids_stays_without_thread_ids() {
        FrameSnapshot frame = Frame() with { ThreadIds = [] };
        FrameSnapshot filtered = FrameLod.Filter(frame, 100L);
        filtered.ThreadIds.Should().BeEmpty();
        filtered.SectionIds.Should().Equal(1, 3);
    }
}
