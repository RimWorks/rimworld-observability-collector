using System;
using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class FrameRingTests {
    private sealed class ManualClock : TimeProvider {
        private long _stamp = 1_000_000L;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _stamp;

        public void Advance(TimeSpan by) => _stamp += by.Ticks;
    }

    // the manual clock never advances, so these rings read as a live stream unless a test
    // advances it past the quiet period.
    private static FrameRing RingWithWindow(int capacity, int window) =>
        new(capacity) { OpenFrameWindow = window, Clock = new ManualClock() };

    // lanes drain up to 100ms apart, so the newest frame is still filling. serving it freezes
    // a truncated frame on a dashboard that never backfills a capture_ordinal it has seen.
    [Fact]
    public void Latest_holds_the_newest_frame_back_while_the_stream_is_live() {
        FrameRing ring = new(64) { Clock = new ManualClock() };

        ring.Add(1, 10, -1, 100, -1, 1000L, 500L);
        ring.Latest().Should().BeNull();

        ring.Add(2, 10, -1, 200, -1, 2000L, 500L);
        ring.Latest()!.CaptureOrdinal.Should().Be(1);

        ring.Add(3, 10, -1, 300, -1, 3000L, 500L);
        ring.Latest()!.CaptureOrdinal.Should().Be(2);

        // only Latest holds back; every other read still serves the whole ring.
        ring.Snapshot().Select(f => f.CaptureOrdinal).Should().Equal(1, 2, 3);
        ring.Range(-1, 10).Select(f => f.CaptureOrdinal).Should().Equal(1, 2, 3);
        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(1, 2, 3);
        ring.FindByOrdinal(3).Should().NotBeNull();
        ring.ComputeStats().NewestOrdinal.Should().Be(3);
    }

    // a paused game sends nothing more, so its last frame has had its drain cycle and is the
    // only thing left to show.
    [Fact]
    public void Latest_serves_the_newest_frame_once_the_stream_goes_quiet() {
        ManualClock clock = new();
        FrameRing ring = new(64) { Clock = clock };
        ring.Add(1, 10, -1, 100, -1, 1000L, 500L);
        ring.Add(2, 10, -1, 200, -1, 2000L, 500L);

        ring.Latest()!.CaptureOrdinal.Should().Be(1);

        clock.Advance(ring.QuietPeriod);

        ring.Latest()!.CaptureOrdinal.Should().Be(2);

        // the stream picking back up holds the newest frame again.
        ring.Add(3, 10, -1, 300, -1, 3000L, 500L);

        ring.Latest()!.CaptureOrdinal.Should().Be(2);
    }

    // holding a frame back is a read-side rule, not a seal: a lane draining late still lands.
    [Fact]
    public void Latest_does_not_move_the_seal_watermark() {
        FrameRing ring = new(64) { Clock = new ManualClock() };
        ring.Add(1, 10, -1, 100, -1, 1000L, 500L, 0L, 1);
        ring.Add(2, 10, -1, 200, -1, 2000L, 500L, 0L, 1);
        ring.Latest();
        ring.Latest();

        ring.Add(2, 20, -1, 201, -1, 2100L, 200L, 0L, 7);

        ring.LateSamples.Should().Be(0);
        ring.FindByOrdinal(2)!.NodeCount.Should().Be(2);
    }

    // lane 7 drains a full interval behind lane 1, so its ordinals land below frames a read
    // already served. they must slot in ascending, not append behind higher ordinals.
    [Fact]
    public void A_lane_draining_behind_a_read_keeps_the_frames_ascending() {
        FrameRing ring = new(8) { Clock = new ManualClock() };
        foreach (int ordinal in new[] { 2, 4, 6 })
            ring.Add(ordinal, 10, -1, ordinal, -1, ordinal * 1000L, 500L, 0L, 1);
        ring.Latest();

        ring.Add(3, 20, -1, 30, -1, 3100L, 200L, 0L, 7);
        ring.Add(5, 20, -1, 50, -1, 5100L, 200L, 0L, 7);

        ring.LateSamples.Should().Be(0);
        ring.Snapshot().Select(f => f.CaptureOrdinal).Should().Equal(2, 3, 4, 5, 6);
        ring.FindByOrdinal(5).Should().NotBeNull();
        ring.Range(3, 10).Select(f => f.CaptureOrdinal).Should().Equal(3, 4, 5, 6);
        ring.Latest()!.CaptureOrdinal.Should().Be(5);
    }

    // the sender drains every 100ms, so the stream never goes quiet during play. reads serve
    // the frame behind the newest, which is the newest one that had a full drain cycle.
    [Fact]
    public void Reads_serve_the_frame_behind_the_newest_while_the_stream_is_live() {
        FrameRing ring = new(64) { Clock = new ManualClock() };
        ring.Add(1, 10, -1, 1, -1, 1000L, 500L, 0L, 1);
        ring.Latest().Should().BeNull();
        for (int ordinal = 2; ordinal <= 5; ordinal++) {
            ring.Add(ordinal, 10, -1, ordinal, -1, ordinal * 1000L, 500L, 0L, 1);
            ring.Latest()!.CaptureOrdinal.Should().Be(ordinal - 1);
        }

        ring.Add(1, 20, -1, 99, -1, 1100L, 200L, 0L, 7);

        ring.LateSamples.Should().Be(0);
        ring.FindByOrdinal(1)!.NodeCount.Should().Be(2);
    }

    // the first frame of a live stream is still filling, so there is nothing complete to serve
    // yet, but it is in the ring for every other read.
    [Fact]
    public void The_newest_frame_is_readable_by_ordinal_the_moment_it_is_reported() {
        FrameRing ring = new(8) { Clock = new ManualClock() };
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);

        ring.FindByOrdinal(1)!.CaptureOrdinal.Should().Be(1);
        ring.Count.Should().Be(1);
    }

    // a lane that reports a drain interval late still has to land, so a frame a read already
    // served keeps taking samples and the next read sees the corrected frame.
    [Fact]
    public void A_frame_already_served_is_corrected_on_the_next_read() {
        FrameRing ring = new(8) { Clock = new ManualClock() };
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        ring.Snapshot().Select(f => f.CaptureOrdinal).Should().Equal(1, 2);

        ring.Add(1, 11, -1, 101, -1, 100L, 5_000_000L);

        ring.LateSamples.Should().Be(0);
        ring.FindByOrdinal(1)!.NodeCount.Should().Be(2);
    }

    // a 500ms hitch frame reports its cheap sections first and the expensive one last. reads
    // in between serve the partial frame, and the hitch node still lands when it drains.
    [Fact]
    public void A_hitch_frame_stays_open_while_other_frames_keep_arriving() {
        FrameRing ring = new(8) { Clock = new ManualClock() };
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);
        ring.Latest()!.CaptureOrdinal.Should().Be(1);

        ring.Add(1, 11, -1, 101, -1, 100L, 5_000_000L);

        ring.LateSamples.Should().Be(0);
        ring.FindByOrdinal(1)!.NodeCount.Should().Be(2);
    }

    // the whole game stalls inside the hitch frame, so nothing at all arrives while the
    // dashboard polls. the read must not commit the frame out from under the expensive node.
    [Fact]
    public void A_frame_read_during_a_stall_still_takes_the_node_that_caused_it() {
        FrameRing ring = new(8) { Clock = new ManualClock() };
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);

        ring.FindByOrdinal(1)!.NodeCount.Should().Be(1);

        ring.Add(1, 11, -1, 101, -1, 100L, 5_000_000L);

        ring.LateSamples.Should().Be(0);
        ring.Count.Should().Be(1);
        ring.FindByOrdinal(1)!.NodeCount.Should().Be(2);
        ring.FindByOrdinal(1)!.DurationTicks.Should().Be(5_000_000L);
    }

    // the game stops sending; the dashboard keeps polling and must see the whole tail, not
    // whatever was sealed a window ago.
    [Fact]
    public void A_paused_stream_serves_every_frame_it_had_open() {
        FrameRing ring = new(64) { Clock = new ManualClock() };
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Snapshot().Select(f => f.CaptureOrdinal).Should().Equal(1, 2, 3, 4, 5);
        ring.ComputeStats().NewestOrdinal.Should().Be(5);
        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(1, 2, 3, 4, 5);
        ring.Range(-1, 10).Select(f => f.CaptureOrdinal).Should().Equal(1, 2, 3, 4, 5);
        ring.FindByOrdinal(5).Should().NotBeNull();
        ring.BaselineMedians(128).Should().ContainKey(10);
    }

    [Fact]
    public void The_open_frame_window_is_settable_and_floors_at_one() {
        FrameRing ring = new(8) { OpenFrameWindow = 4 };

        ring.OpenFrameWindow.Should().Be(4);

        ring.OpenFrameWindow = 0;

        ring.OpenFrameWindow.Should().Be(1);
    }

    // the sender drains one thread lane per call, so lane 7 replays the same frames lane 1
    // already reported. every frame has to keep both lanes' nodes.
    [Fact]
    public void Frames_keep_the_nodes_of_every_lane_that_reports_them() {
        FrameRing ring = new(8) { Clock = new ManualClock() };
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 10, -1, ordinal * 1000L, 500L, 0L, 1);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 20, -1, ordinal * 10 + 1, -1, ordinal * 1000L + 100L, 200L, 0L, 7);

        ring.Flush();

        ring.LateSamples.Should().Be(0);
        FrameSnapshot[] frames = ring.Snapshot();
        frames.Select(f => f.CaptureOrdinal).Should().Equal(1, 2, 3);
        foreach (FrameSnapshot frame in frames)
            frame.ThreadIds.Should().Equal(1, 7);
    }

    [Fact]
    public void A_frame_older_than_the_open_window_is_late() {
        FrameRing ring = RingWithWindow(64, 4);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal, -1, ordinal * 1000L, 500L, 0L, 1);

        ring.Add(2, 20, -1, 99, -1, 2100L, 200L, 0L, 7);
        ring.Add(5, 20, -1, 98, -1, 5100L, 200L, 0L, 7);
        ring.Flush();

        ring.LateSamples.Should().Be(1);
        ring.FindByOrdinal(5)!.ThreadIds.Should().Equal(1, 7);
    }

    // the window is the only rule while samples keep arriving, so a wide window really is
    // wide: 39 newer frames and 39 reads later, lane 7's drain for frame 1 still lands.
    [Fact]
    public void A_wide_window_holds_a_frame_open_across_many_reads() {
        FrameRing ring = RingWithWindow(64, 64);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L, 0L, 1);

        for (int ordinal = 2; ordinal <= 40; ordinal++) {
            ring.Add(ordinal, 10, -1, ordinal * 10, -1, ordinal * 1000L, 500L, 0L, 1);
            ring.Latest()!.CaptureOrdinal.Should().Be(ordinal - 1);
        }

        ring.Add(1, 20, -1, 999, -1, 150L, 200L, 0L, 7);

        ring.LateSamples.Should().Be(0);
        ring.Flush();
        ring.FindByOrdinal(1)!.ThreadIds.Should().Equal(1, 7);
    }

    [Fact]
    public void A_frame_seals_when_a_higher_ordinal_arrives() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        ring.Add(1, 30, -1, 102, -1, 300L, 100L);

        ring.LateSamples.Should().Be(1);
        FrameSnapshot sealedFrame = ring.FindByOrdinal(1)!;
        sealedFrame.NodeCount.Should().Be(2);
        sealedFrame.SectionIds.Should().Equal(10, 20);
        sealedFrame.ParentIds.Should().Equal(-1, 10);
    }

    [Fact]
    public void A_sealed_frame_carries_node_ids_and_parent_node_ids() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);

        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        FrameSnapshot sealedFrame = ring.FindByOrdinal(1)!;
        sealedFrame.NodeIds.Should().Equal(100, 101);
        sealedFrame.ParentNodeIds.Should().Equal(-1, 100);
    }

    [Fact]
    public void Sealing_a_second_frame_does_not_leak_node_ids_from_the_first() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);
        ring.Add(2, 30, -1, 200, -1, 700L, 400L);
        ring.Add(3, 10, -1, 300, -1, 900L, 100L);

        FrameSnapshot secondFrame = ring.FindByOrdinal(2)!;
        secondFrame.NodeIds.Should().Equal(200);
        secondFrame.ParentNodeIds.Should().Equal(-1);
    }

    [Fact]
    public void Frame_bounds_span_the_earliest_start_to_the_latest_end() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(1, 20, 10, 101, 100, 150L, 200L);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        FrameSnapshot frame = ring.FindByOrdinal(1)!;

        frame.StartTicks.Should().Be(100L);
        frame.EndTicks.Should().Be(600L);
        frame.DurationTicks.Should().Be(500L);
    }

    [Fact]
    public void Samples_from_before_the_first_play_frame_are_counted_and_skipped() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(0, 10, -1, 100, -1, 1L, 9_000_000L);
        ring.Add(1, 10, -1, 100, -1, 100L, 500L);
        ring.Add(2, 10, -1, 200, -1, 700L, 400L);

        ring.PreFrameSamples.Should().Be(1);
        ring.Latest()!.CaptureOrdinal.Should().Be(1);
        ring.FindByOrdinal(1)!.NodeCount.Should().Be(1);
    }

    [Fact]
    public void A_sample_for_an_already_sealed_frame_is_counted_and_dropped() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(2, 10, -1, 100, -1, 100L, 500L);
        ring.Add(3, 10, -1, 200, -1, 700L, 400L);

        ring.Add(1, 99, -1, 300, -1, 10L, 10L);

        ring.LateSamples.Should().Be(1);
        ring.Count.Should().Be(2);
        ring.Snapshot().Select(f => f.CaptureOrdinal).Should().Equal(2, 3);
    }

    [Fact]
    public void The_ring_overwrites_the_oldest_frame_at_capacity() {
        FrameRing ring = RingWithWindow(2, 1);
        for (int ordinal = 1; ordinal <= 4; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);

        // 4 is still filling, so the newest frame Latest can serve is 3.
        ring.Latest()!.CaptureOrdinal.Should().Be(3);

        FrameRingStats stats = ring.ComputeStats();

        // frames 2 and 3 are what the ring kept; 4 is still open and previews on top.
        stats.FrameCount.Should().Be(3);
        stats.OldestOrdinal.Should().Be(2);
        stats.NewestOrdinal.Should().Be(4);
    }

    // the bundle exporter snapshots the ring once and must price that snapshot, not the ring,
    // which keeps taking frames while the zip is being written.
    [Fact]
    public void StatsFor_describes_the_array_it_is_given_not_the_live_ring() {
        FrameRing ring = RingWithWindow(8, 1);
        for (int ordinal = 1; ordinal <= 4; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);

        FrameSnapshot[] snapshot = ring.Snapshot();
        ring.Add(9, 10, -1, 900, -1, 9000L, 100L);
        ring.Add(10, 10, -1, 1000, -1, 10000L, 100L);

        FrameRingStats fromSnapshot = FrameRing.StatsFor(snapshot);

        fromSnapshot.FrameCount.Should().Be(snapshot.Length);
        fromSnapshot.NewestOrdinal.Should().Be(4);
        ring.ComputeStats().NewestOrdinal.Should().Be(10);
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
        FrameRing ring = RingWithWindow(8, 1);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 100L);

        ring.FindByOrdinal(3)!.CaptureOrdinal.Should().Be(3);
        ring.FindByOrdinal(1)!.CaptureOrdinal.Should().Be(1);
    }

    // ordinals skip whenever a frame carried no samples, so the search cannot assume
    // ordinal minus oldest is an offset.
    [Fact]
    public void FindByOrdinal_handles_gaps_and_misses() {
        FrameRing ring = RingWithWindow(8, 1);
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
        RingWithWindow(8, 1).FindByOrdinal(1).Should().BeNull();
    }

    [Fact]
    public void SnapshotStrip_returns_the_newest_frames_oldest_first() {
        FrameRing ring = RingWithWindow(8, 1);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, ordinal * 10L);

        (int Ordinal, long DurationTicks)[] strip = ring.SnapshotStrip(3);

        strip.Select(e => e.Ordinal).Should().Equal(4, 5, 6);
        strip[0].DurationTicks.Should().Be(40);
    }

    [Fact]
    public void SnapshotStrip_caps_at_what_the_ring_holds() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(1, 10, -1, 100, -1, 1000L, 50L);
        ring.Add(2, 10, -1, 200, -1, 2000L, 50L);

        ring.SnapshotStrip(100).Should().HaveCount(2);
        ring.SnapshotStrip(0).Should().HaveCount(2);
        RingWithWindow(8, 1).SnapshotStrip(10).Should().BeEmpty();
    }

    [Fact]
    public void BaselineMedians_takes_the_median_of_each_sections_per_frame_total() {
        FrameRing ring = RingWithWindow(16, 1);
        // section 10 costs 100, 200 then 300 ticks across three frames.
        long[] costs = [100, 200, 300];
        for (int f = 0; f < 3; f++)
            ring.Add(f + 1, 10, -1, f + 1, -1, f * 1000L, costs[f]);

        ring.BaselineMedians(128)[10].Should().Be(200);
    }

    // a section that runs twice in one frame costs the sum of both, not either one.
    [Fact]
    public void BaselineMedians_sums_repeats_within_a_frame_before_taking_the_median() {
        FrameRing ring = RingWithWindow(16, 1);
        ring.Add(1, 10, -1, 1, -1, 0L, 50L);
        ring.Add(1, 10, -1, 2, -1, 100L, 70L);
        ring.Add(2, 10, -1, 3, -1, 1000L, 90L);

        // frame totals are 120 and 90; unsummed repeats would put 50 or 70 in the pot.
        ring.BaselineMedians(128)[10].Should().Be(120);
    }

    [Fact]
    public void BaselineMedians_only_walks_the_newest_frames_it_was_asked_for() {
        FrameRing ring = RingWithWindow(16, 1);
        for (int f = 1; f <= 5; f++)
            ring.Add(f, 10, -1, f, -1, f * 1000L, f * 100L);

        // frames 3, 4 and 5 cost 300, 400 and 500, so the window median is 400.
        ring.BaselineMedians(3)[10].Should().Be(400);
    }

    [Fact]
    public void BaselineMedians_on_an_empty_ring_returns_nothing() {
        RingWithWindow(8, 1).BaselineMedians(128).Should().BeEmpty();
    }

    [Fact]
    public void Clear_resets_the_counters_and_reopens_at_a_lower_ordinal() {
        FrameRing ring = RingWithWindow(8, 1);
        ring.Add(0, 10, -1, 100, -1, 1L, 5L);
        ring.Add(5, 10, -1, 100, -1, 100L, 500L);
        ring.Add(6, 10, -1, 200, -1, 700L, 400L);
        ring.Add(1, 99, -1, 300, -1, 10L, 10L);

        ring.Count.Should().Be(2);
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
        FrameRing ring = RingWithWindow(8, 1);
        long[] durations = [100L, 900L, 200L, 300L];
        for (int i = 0; i < durations.Length; i++)
            ring.Add(i + 1, 10, -1, i * 100, -1, i * 10_000L, durations[i]);

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
        FrameRing ring = RingWithWindow(8, 1);

        FrameRingStats stats = ring.ComputeStats();

        stats.FrameCount.Should().Be(0);
        stats.NewestOrdinal.Should().Be(-1);
        stats.OldestOrdinal.Should().Be(-1);
    }

    [Fact]
    public void Snapshot_returns_every_frame_oldest_first() {
        FrameRing ring = RingWithWindow(4, 1);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        FrameSnapshot[] frames = ring.Snapshot();

        frames.Select(f => f.CaptureOrdinal).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Snapshot_drops_the_frames_the_ring_overwrote() {
        FrameRing ring = RingWithWindow(2, 1);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        FrameSnapshot[] frames = ring.Snapshot();

        frames.Select(f => f.CaptureOrdinal).Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Snapshot_keeps_ring_order_when_the_write_head_is_mid_buffer() {
        FrameRing ring = RingWithWindow(3, 1);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        FrameSnapshot[] frames = ring.Snapshot();

        frames.Select(f => f.CaptureOrdinal).Should().Equal(2, 3, 4, 5);
    }

    [Fact]
    public void Snapshot_serves_the_open_frame_before_it_seals() {
        FrameRing ring = RingWithWindow(4, 1);
        ring.Add(1, 10, -1, 100, -1, 1000L, 500L);

        ring.Snapshot().Select(f => f.CaptureOrdinal).Should().Equal(1);
    }

    private static FrameRing RingOfOrdinals(int capacity, params int[] ordinals) {
        FrameRing ring = RingWithWindow(capacity, 1);
        foreach (int ordinal in ordinals)
            ring.Add(ordinal, 10, -1, ordinal, -1, ordinal * 1000L, 500L);
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

        ring.Range(1, 10).Select(f => f.CaptureOrdinal).Should().Equal(2, 3, 4, 5);
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
        RingWithWindow(8, 1).Range(-1, 10).Should().BeEmpty();
    }

    // resizing is a live setting, so the frames already captured have to survive whatever fits.
    [Fact]
    public void Growing_the_ring_keeps_every_frame_it_held() {
        FrameRing ring = RingWithWindow(4, 1);
        for (int ordinal = 1; ordinal <= 5; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(16);

        ring.Capacity.Should().Be(16);
        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Shrinking_the_ring_keeps_the_newest_frames_that_fit() {
        FrameRing ring = RingWithWindow(8, 1);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(2);

        ring.Capacity.Should().Be(2);
        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(4, 5, 6);
    }

    [Fact]
    public void Resizing_to_the_same_capacity_leaves_the_ring_alone() {
        FrameRing ring = RingWithWindow(4, 1);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(4);

        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(1, 2, 3);
    }

    // a resized ring still has to wrap correctly, or the strip reorders after the next writes.
    [Fact]
    public void A_resized_ring_keeps_wrapping_in_ordinal_order() {
        FrameRing ring = RingWithWindow(8, 1);
        for (int ordinal = 1; ordinal <= 6; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Resize(3);
        for (int ordinal = 7; ordinal <= 9; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(6, 7, 8, 9);
    }

    [Fact]
    public void The_strip_returns_every_frame_the_ring_holds() {
        FrameRing ring = RingWithWindow(2000, 1);
        for (int ordinal = 1; ordinal <= 900; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.SnapshotStrip(0).Should().HaveCount(900);
    }

    // clearing is a user action that throws away history, so it has to actually empty the ring
    // rather than just reset the write cursor.
    [Fact]
    public void Clearing_empties_the_strip_and_the_stats() {
        FrameRing ring = RingWithWindow(8, 1);
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
        FrameRing ring = RingWithWindow(4, 1);
        for (int ordinal = 1; ordinal <= 3; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.Clear();
        for (int ordinal = 7; ordinal <= 9; ordinal++)
            ring.Add(ordinal, 10, -1, ordinal * 100, -1, ordinal * 1000L, 500L);

        ring.SnapshotStrip(0).Select(f => f.Ordinal).Should().Equal(7, 8, 9);
    }
}
