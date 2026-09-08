using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class RingBufferTests {
    [Fact]
    public void Write_then_drain_returns_same_values_in_order() {
        SampleRingBuffer ring = new(16);
        for (int i = 0; i < 10; i++) {
            ring.TryWrite(i, i * 7, i * 3, i * 3 - 1, i * 100L, i * 1000L, 1).Should().BeTrue();
        }

        SampleBatch batch = new SampleBatch(16);
        var ids = batch.SectionIds;
        var parents = batch.ParentIds;
        var starts = batch.StartTimestamps;
        var elapsed = batch.ElapsedTicks;
        int n = ring.Drain(batch, 16);

        n.Should().Be(10);
        for (int i = 0; i < 10; i++) {
            ids[i].Should().Be(i);
            parents[i].Should().Be(i * 7);
            starts[i].Should().Be(i * 100L);
            elapsed[i].Should().Be(i * 1000L);
        }
    }

    [Fact]
    public void Write_then_drain_round_trips_node_ids() {
        SampleRingBuffer ring = new(16);
        ring.TryWrite(1, -1, 100, -1, 0L, 0L, 1).Should().BeTrue();
        ring.TryWrite(2, 1, 101, 100, 0L, 0L, 1).Should().BeTrue();

        SampleBatch batch = new SampleBatch(16);
        var nodeIds = batch.NodeIds;
        var parentNodeIds = batch.ParentNodeIds;
        int n = ring.Drain(batch, 16);

        n.Should().Be(2);
        nodeIds[0].Should().Be(100);
        parentNodeIds[0].Should().Be(-1);
        nodeIds[1].Should().Be(101);
        parentNodeIds[1].Should().Be(100);
    }

    [Fact]
    public void Drain_fills_node_id_arrays_in_write_order() {
        SampleRingBuffer ring = new(16);
        for (int i = 0; i < 5; i++)
            ring.TryWrite(i, -1, i * 10, i * 10 - 1, 0L, 0L, 1).Should().BeTrue();

        SampleBatch batch = new SampleBatch(16);
        var nodeIds = batch.NodeIds;
        var parentNodeIds = batch.ParentNodeIds;
        int n = ring.Drain(batch, 16);

        n.Should().Be(5);
        for (int i = 0; i < 5; i++) {
            nodeIds[i].Should().Be(i * 10);
            parentNodeIds[i].Should().Be(i * 10 - 1);
        }
    }

    [Fact]
    public void Drops_when_full_and_increments_dropped_counter() {
        SampleRingBuffer ring = new(4);
        for (int i = 0; i < 4; i++)
            ring.TryWrite(i, -1, 0, -1, 0, 0, 1).Should().BeTrue();

        ring.TryWrite(99, -1, 0, -1, 0, 0, 1).Should().BeFalse();
        ring.Dropped.Should().Be(1);
    }

    [Fact]
    public void Multiple_drain_cycles_progress_read_pointer() {
        SampleRingBuffer ring = new(8);
        SampleBatch batch = new SampleBatch(8);
        var ids = batch.SectionIds;

        ring.TryWrite(1, -1, 0, -1, 0, 0, 1);
        ring.TryWrite(2, -1, 0, -1, 0, 0, 1);
        ring.Drain(batch, 8).Should().Be(2);

        ring.TryWrite(3, -1, 0, -1, 0, 0, 1);
        ring.TryWrite(4, -1, 0, -1, 0, 0, 1);
        ring.Drain(batch, 8).Should().Be(2);
        ids[0].Should().Be(3);
        ids[1].Should().Be(4);
    }

    [Fact]
    public void Drain_returns_the_frame_ordinal_each_sample_was_written_with() {
        SampleRingBuffer ring = new(16);
        ring.TryWrite(1, -1, 0, -1, 100L, 10L, 7).Should().BeTrue();
        ring.TryWrite(2, 1, 0, -1, 110L, 20L, 7).Should().BeTrue();
        ring.TryWrite(3, -1, 0, -1, 200L, 30L, 8).Should().BeTrue();

        SampleBatch batch = new SampleBatch(16);
        var ordinals = batch.FrameOrdinals;
        int n = ring.Drain(batch, 16);

        n.Should().Be(3);
        ordinals[0].Should().Be(7);
        ordinals[1].Should().Be(7);
        ordinals[2].Should().Be(8);
    }

    // SampleBatch sizes all seven arrays together, so a short-array mismatch is no longer
    // reachable. What still needs guarding is that a drain never runs past the batch.
    [Fact]
    public void Drain_is_capped_by_the_batch_capacity() {
        SampleRingBuffer ring = new(16);
        for (int i = 0; i < 5; i++)
            ring.TryWrite(i, -1, i, -1, 0L, 0L, 1).Should().BeTrue();

        SampleBatch small = new SampleBatch(2);
        int n = ring.Drain(small, 16);

        n.Should().Be(2);
        small.SectionIds[0].Should().Be(0);
        small.SectionIds[1].Should().Be(1);

        // the rest stay queued for the next drain rather than being dropped
        SampleBatch rest = new SampleBatch(16);
        ring.Drain(rest, 16).Should().Be(3);
        rest.SectionIds[0].Should().Be(2);
    }

    [Fact]
    public void Drain_is_capped_by_max_count_below_capacity() {
        SampleRingBuffer ring = new(16);
        for (int i = 0; i < 5; i++)
            ring.TryWrite(i, -1, i, -1, 0L, 0L, 1).Should().BeTrue();

        SampleBatch batch = new SampleBatch(16);
        ring.Drain(batch, 2).Should().Be(2);
        batch.SectionIds[0].Should().Be(0);
        ring.Drain(batch, 16).Should().Be(3);
    }
}
