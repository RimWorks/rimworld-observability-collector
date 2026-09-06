using RimWorks.RimObs.Collector.Aggregation;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class FrameRingTests {
    [Fact]
    public void A_frame_seals_when_a_higher_ordinal_arrives() {
        FrameRing ring = new(8);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);

        ring.Latest().Should().BeNull();

        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        FrameSnapshot? sealedFrame = ring.Latest();
        sealedFrame.Should().NotBeNull();
        sealedFrame!.CaptureOrdinal.Should().Be(1);
        sealedFrame.NodeCount.Should().Be(2);
        sealedFrame.SectionIds.Should().Equal(10, 20);
        sealedFrame.ParentIds.Should().Equal(-1, 10);
    }

    [Fact]
    public void A_sealed_frame_carries_node_ids_and_parent_node_ids() {
        FrameRing ring = new(8);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);

        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        FrameSnapshot sealedFrame = ring.Latest()!;
        sealedFrame.NodeIds.Should().Equal(100, 101);
        sealedFrame.ParentNodeIds.Should().Equal(-1, 100);
    }

    [Fact]
    public void Frame_bounds_span_the_earliest_start_to_the_latest_end() {
        FrameRing ring = new(8);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        FrameSnapshot frame = ring.Latest()!;

        frame.StartTicks.Should().Be(100L);
        frame.EndTicks.Should().Be(600L);
        frame.DurationTicks.Should().Be(500L);
    }

    [Fact]
    public void Samples_from_before_the_first_play_frame_are_counted_and_skipped() {
        FrameRing ring = new(8);
        ring.Add(0, 10, -1, 100, -1, 1L, 9_000_000L);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        ring.PreFrameSamples.Should().Be(1);
        ring.Latest()!.CaptureOrdinal.Should().Be(1);
        ring.Latest()!.NodeCount.Should().Be(1);
    }

    [Fact]
    public void A_sample_for_an_already_sealed_frame_is_counted_and_dropped() {
        FrameRing ring = new(8);
        ring.Add(2, 10, -1, 100, -1, 100L, 500L);
        ring.Add(3, 10, -1, 200, -1, 700L, 400L);

        ring.Add(1, 99, -1, 300, -1, 10L, 10L);

        ring.LateSamples.Should().Be(1);
        ring.Count.Should().Be(1);
    }

    [Fact]
    public void The_ring_overwrites_the_oldest_frame_at_capacity() {
        FrameRing ring = new(2);
        for (int ordinal = 1; ordinal <= 4; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);

        ring.Count.Should().Be(2);
        ring.Latest()!.CaptureOrdinal.Should().Be(3);

        FrameRingStats stats = ring.ComputeStats();

        stats.FrameCount.Should().Be(2);
        stats.OldestOrdinal.Should().Be(2);
        stats.NewestOrdinal.Should().Be(3);
    }

    [Fact]
    public void Clear_resets_the_counters_and_reopens_at_a_lower_ordinal() {
        FrameRing ring = new(8);
        ring.Add(0, 10, -1, 100, -1, 1L, 5L);
        ring.Add(5, 10, -1, 100, -1, 100L, 500L);
        ring.Add(6, 10, -1, 200, -1, 700L, 400L);
        ring.Add(1, 99, -1, 300, -1, 10L, 10L);

        ring.Count.Should().Be(1);
        ring.PreFrameSamples.Should().Be(1);
        ring.LateSamples.Should().Be(1);

        ring.Clear();

        ring.Count.Should().Be(0);
        ring.PreFrameSamples.Should().Be(0);
        ring.LateSamples.Should().Be(0);
        ring.Latest().Should().BeNull();
        ring.ComputeStats().FrameCount.Should().Be(0);

        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        ring.Latest()!.CaptureOrdinal.Should().Be(1);
        ring.LateSamples.Should().Be(0);
    }

    [Fact]
    public void Stats_report_ordinal_bounds_and_percentiles_over_the_ring() {
        FrameRing ring = new(8);
        long[] durations = [100L, 900L, 200L, 300L];
        for (int i = 0; i < durations.Length; i++)
            ring.Add(i + 1, 10, -1, i * 100, -1, i * 10_000L, durations[i]);
        ring.Add(durations.Length + 1, 10, -1, 400, -1, 90_000L, 50L);

        FrameRingStats stats = ring.ComputeStats();

        stats.FrameCount.Should().Be(4);
        stats.OldestOrdinal.Should().Be(1);
        stats.NewestOrdinal.Should().Be(4);
        stats.MinDurationTicks.Should().Be(100L);
        stats.MaxDurationTicks.Should().Be(900L);
        stats.MedianDurationTicks.Should().Be(300L);
        stats.P99DurationTicks.Should().Be(900L);
    }

    [Fact]
    public void Stats_on_an_empty_ring_report_no_frames() {
        FrameRing ring = new(8);

        FrameRingStats stats = ring.ComputeStats();

        stats.FrameCount.Should().Be(0);
        stats.NewestOrdinal.Should().Be(-1);
        stats.OldestOrdinal.Should().Be(-1);
    }
}
