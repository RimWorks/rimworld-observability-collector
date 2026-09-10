using System.Linq;
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
    public void Sealing_a_second_frame_does_not_leak_node_ids_from_the_first() {
        FrameRing ring = new(8);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);
        ring.Add(2, 30, -1, 200, -1, 700L, 400L);
        ring.Add(3, 10, -1, 300, -1, 900L, 100L);

        FrameSnapshot secondFrame = ring.Latest()!;
        secondFrame.CaptureOrdinal.Should().Be(2);
        secondFrame.NodeIds.Should().Equal(200);
        secondFrame.ParentNodeIds.Should().Equal(-1);
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

    // the bundle exporter snapshots the ring once and must price that snapshot, not the ring,
    // which keeps sealing frames while the zip is being written.
    [Fact]
    public void StatsFor_describes_the_array_it_is_given_not_the_live_ring() {
        FrameRing ring = new(8);
        for (int ordinal = 1; ordinal <= 4; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);

        FrameSnapshot[] snapshot = ring.Snapshot();
        ring.Add(9, 10, -1, 900, -1, 9000L, 100L);
        ring.Add(10, 10, -1, 1000, -1, 10000L, 100L);

        FrameRingStats fromSnapshot = FrameRing.StatsFor(snapshot);

        fromSnapshot.FrameCount.Should().Be(snapshot.Length);
        fromSnapshot.NewestOrdinal.Should().Be(3);
        ring.ComputeStats().NewestOrdinal.Should().Be(9);
    }

    [Fact]
    public void StatsFor_reports_an_empty_array_as_an_empty_ring() {
        FrameRingStats stats = FrameRing.StatsFor([]);

        stats.FrameCount.Should().Be(0);
        stats.NewestOrdinal.Should().Be(-1);
        stats.OldestOrdinal.Should().Be(-1);
    }

    [Fact]
    public void FindByOrdinal_returns_the_frame_with_that_ordinal() {
        FrameRing ring = new(8);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);

        ring.FindByOrdinal(3)!.CaptureOrdinal.Should().Be(3);
        ring.FindByOrdinal(1)!.CaptureOrdinal.Should().Be(1);
    }

    // ordinals skip whenever a frame carried no samples, so the search cannot assume
    // ordinal minus oldest is an offset.
    [Fact]
    public void FindByOrdinal_handles_gaps_and_misses() {
        FrameRing ring = new(8);
        foreach (int ordinal in new[] { 2, 7, 9, 40 })
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);
        ring.Add(99, 10, -1, 900, -1, 99000L, 100L);

        ring.FindByOrdinal(9)!.CaptureOrdinal.Should().Be(9);
        ring.FindByOrdinal(40)!.CaptureOrdinal.Should().Be(40);
        ring.FindByOrdinal(8).Should().BeNull();
        ring.FindByOrdinal(1000).Should().BeNull();
    }

    [Fact]
    public void FindByOrdinal_on_an_empty_ring_returns_null() {
        new FrameRing(8).FindByOrdinal(1).Should().BeNull();
    }

    [Fact]
    public void SnapshotStrip_returns_the_newest_frames_oldest_first() {
        FrameRing ring = new(8);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, ordinal * 10L);

        (int Ordinal, long DurationTicks)[] strip = ring.SnapshotStrip(3);

        strip.Select(e => e.Ordinal).Should().Equal(3, 4, 5);
        strip[0].DurationTicks.Should().Be(30);
    }

    [Fact]
    public void SnapshotStrip_caps_at_what_the_ring_holds() {
        FrameRing ring = new(8);
        ring.Add(1, 10, -1, 100, -1, 1000L, 50L);
        ring.Add(2, 10, -1, 200, -1, 2000L, 50L);

        ring.SnapshotStrip(100).Should().HaveCount(1);
        ring.SnapshotStrip(0).Should().HaveCount(1);
        new FrameRing(8).SnapshotStrip(10).Should().BeEmpty();
    }

    [Fact]
    public void BaselineMedians_takes_the_median_of_each_sections_per_frame_total() {
        FrameRing ring = new(16);
        // section 10 costs 100, 200 then 300 ticks across three sealed frames.
        long[] costs = [100, 200, 300];
        for (int f = 0; f < 3; f++)
            ring.Add(f + 1, 10, -1, f + 1, -1, f * 1000L, costs[f]);
        ring.Add(99, 10, -1, 99, -1, 99000L, 1L);

        ring.BaselineMedians(128)[10].Should().Be(200);
    }

    // a section that runs twice in one frame costs the sum of both, not either one.
    [Fact]
    public void BaselineMedians_sums_repeats_within_a_frame_before_taking_the_median() {
        FrameRing ring = new(16);
        ring.Add(1, 10, -1, 1, -1, 0L, 50L);
        ring.Add(1, 10, -1, 2, -1, 100L, 70L);
        ring.Add(2, 10, -1, 3, -1, 1000L, 500L);

        ring.BaselineMedians(128)[10].Should().Be(120);
    }

    [Fact]
    public void BaselineMedians_only_walks_the_newest_frames_it_was_asked_for() {
        FrameRing ring = new(16);
        for (int f = 1; f <= 5; f++)
            ring.Add(f, 10, -1, f, -1, f * 1000L, f * 100L);
        ring.Add(99, 10, -1, 99, -1, 99000L, 1L);

        // frames 3, 4 and 5 cost 300, 400 and 500, so the window median is 400.
        ring.BaselineMedians(3)[10].Should().Be(400);
    }

    [Fact]
    public void BaselineMedians_on_an_empty_ring_returns_nothing() {
        new FrameRing(8).BaselineMedians(128).Should().BeEmpty();
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

    [Fact]
    public void Snapshot_returns_every_sealed_frame_oldest_first() {
        FrameRing ring = new(4);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        FrameSnapshot[] frames = ring.Snapshot();

        frames.Should().HaveCount(2);
        frames[0].CaptureOrdinal.Should().Be(1);
        frames[1].CaptureOrdinal.Should().Be(2);
    }

    [Fact]
    public void Snapshot_drops_the_frames_the_ring_overwrote() {
        FrameRing ring = new(2);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        FrameSnapshot[] frames = ring.Snapshot();

        frames.Should().HaveCount(2);
        frames[0].CaptureOrdinal.Should().Be(3);
        frames[1].CaptureOrdinal.Should().Be(4);
    }

    [Fact]
    public void Snapshot_keeps_ring_order_when_the_write_head_is_mid_buffer() {
        FrameRing ring = new(3);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        FrameSnapshot[] frames = ring.Snapshot();

        frames.Select(f => f.CaptureOrdinal).Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Snapshot_is_empty_before_the_first_frame_seals() {
        FrameRing ring = new(4);
        ring.Add(1, 10, -1, 100, -1, 1000L, 500L);

        ring.Snapshot().Should().BeEmpty();
    }

    private static FrameRing RingOfOrdinals(int capacity, params int[] ordinals) {
        FrameRing ring = new(capacity);
        foreach (int ordinal in ordinals)
            ring.Add(ordinal, 10, -1, ordinal, -1, ordinal * 1000L, 500L);
        // the last frame stays open until a higher ordinal lands, so close it out.
        ring.Add(int.MaxValue, 10, -1, 0, -1, long.MaxValue / 2, 1L);
        return ring;
    }

    [Fact]
    public void Range_returns_a_run_of_frames_ascending_from_the_asked_ordinal() {
        FrameRing ring = RingOfOrdinals(16, 1, 2, 3, 4, 5);

        ring.Range(2, 3).Select(f => f.CaptureOrdinal).Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Range_with_a_negative_from_returns_the_newest_frames() {
        FrameRing ring = RingOfOrdinals(16, 1, 2, 3, 4, 5);

        ring.Range(-1, 2).Select(f => f.CaptureOrdinal).Should().Equal(4, 5);
    }

    [Fact]
    public void Range_clips_to_the_oldest_frame_still_held_when_from_was_evicted() {
        FrameRing ring = RingOfOrdinals(3, 1, 2, 3, 4, 5);

        ring.Range(1, 10).Select(f => f.CaptureOrdinal).Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Range_skips_the_ordinals_no_frame_was_recorded_for() {
        FrameRing ring = RingOfOrdinals(16, 1, 5, 9);

        ring.Range(2, 10).Select(f => f.CaptureOrdinal).Should().Equal(5, 9);
    }

    [Fact]
    public void Range_is_empty_past_the_newest_ordinal() {
        FrameRing ring = RingOfOrdinals(16, 1, 2, 3);

        ring.Range(int.MaxValue - 1, 4).Should().BeEmpty();
    }

    [Fact]
    public void Range_is_empty_on_an_empty_ring() {
        new FrameRing(8).Range(-1, 10).Should().BeEmpty();
    }

    // resizing is a live setting, so the frames already captured have to survive whatever fits.
    [Fact]
    public void Growing_the_ring_keeps_every_frame_it_held() {
        FrameRing ring = new(4);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(16);

        ring.Capacity.Should().Be(16);
        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Shrinking_the_ring_keeps_the_newest_frames_that_fit() {
        FrameRing ring = new(8);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(2);

        ring.Capacity.Should().Be(2);
        ring.Count.Should().Be(2);
        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(4, 5);
    }

    [Fact]
    public void Resizing_to_the_same_capacity_leaves_the_ring_alone() {
        FrameRing ring = new(4);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(4);

        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(1, 2);
    }

    // a resized ring still has to wrap correctly, or the strip reorders after the next writes.
    [Fact]
    public void A_resized_ring_keeps_wrapping_in_ordinal_order() {
        FrameRing ring = new(8);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(3);
        for (int ordinal = 7; ordinal <= 9; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(6, 7, 8);
    }

    [Fact]
    public void The_strip_returns_every_frame_the_ring_holds() {
        FrameRing ring = new(2000);
        for (int ordinal = 1; ordinal <= 900; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.SnapshotStrip(0).Should().HaveCount(899);
    }

    // clearing is a user action that throws away history, so it has to actually empty the ring
    // rather than just reset the write cursor.
    [Fact]
    public void Clearing_empties_the_strip_and_the_stats() {
        FrameRing ring = new(8);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Clear();

        ring.Count.Should().Be(0);
        ring.SnapshotStrip(0).Should().BeEmpty();
        ring.Latest().Should().BeNull();
        ring.ComputeStats().FrameCount.Should().Be(0);
    }

    [Fact]
    public void A_cleared_ring_keeps_capturing_from_the_next_frame() {
        FrameRing ring = new(4);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Clear();
        for (int ordinal = 7; ordinal <= 9; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(7, 8);
    }
}
