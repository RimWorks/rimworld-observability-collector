using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class RingBufferTests {
    [Fact]
    public void Write_then_drain_returns_same_values_in_order() {
        SampleRingBuffer ring = new(16);
        for (int i = 0; i < 10; i++) {
            ring.TryWrite(i, i * 7, i * 100L, i * 1000L, 1).Should().BeTrue();
        }

        int[] ids = new int[16];
        int[] parents = new int[16];
        long[] starts = new long[16];
        long[] elapsed = new long[16];
        int[] ordinals = new int[16];
        int n = ring.Drain(ids, parents, starts, elapsed, ordinals, 16);

        n.Should().Be(10);
        for (int i = 0; i < 10; i++) {
            ids[i].Should().Be(i);
            parents[i].Should().Be(i * 7);
            starts[i].Should().Be(i * 100L);
            elapsed[i].Should().Be(i * 1000L);
        }
    }

    [Fact]
    public void Drops_when_full_and_increments_dropped_counter() {
        SampleRingBuffer ring = new(4);
        for (int i = 0; i < 4; i++)
            ring.TryWrite(i, -1, 0, 0, 1).Should().BeTrue();

        ring.TryWrite(99, -1, 0, 0, 1).Should().BeFalse();
        ring.Dropped.Should().Be(1);
    }

    [Fact]
    public void Multiple_drain_cycles_progress_read_pointer() {
        SampleRingBuffer ring = new(8);
        int[] ids = new int[8];
        int[] parents = new int[8];
        long[] starts = new long[8];
        long[] elapsed = new long[8];
        int[] ordinals = new int[8];

        ring.TryWrite(1, -1, 0, 0, 1);
        ring.TryWrite(2, -1, 0, 0, 1);
        ring.Drain(ids, parents, starts, elapsed, ordinals, 8).Should().Be(2);

        ring.TryWrite(3, -1, 0, 0, 1);
        ring.TryWrite(4, -1, 0, 0, 1);
        ring.Drain(ids, parents, starts, elapsed, ordinals, 8).Should().Be(2);
        ids[0].Should().Be(3);
        ids[1].Should().Be(4);
    }

    [Fact]
    public void Drain_returns_the_frame_ordinal_each_sample_was_written_with() {
        SampleRingBuffer ring = new(16);
        ring.TryWrite(1, -1, 100L, 10L, 7).Should().BeTrue();
        ring.TryWrite(2, 1, 110L, 20L, 7).Should().BeTrue();
        ring.TryWrite(3, -1, 200L, 30L, 8).Should().BeTrue();

        int[] ids = new int[16];
        int[] parents = new int[16];
        long[] starts = new long[16];
        long[] elapsed = new long[16];
        int[] ordinals = new int[16];
        int n = ring.Drain(ids, parents, starts, elapsed, ordinals, 16);

        n.Should().Be(3);
        ordinals[0].Should().Be(7);
        ordinals[1].Should().Be(7);
        ordinals[2].Should().Be(8);
    }

    [Fact]
    public void Drain_is_capped_by_the_shortest_destination_array() {
        SampleRingBuffer ring = new(16);
        for (int i = 0; i < 5; i++)
            ring.TryWrite(i, -1, 0L, 0L, 1).Should().BeTrue();

        int n = ring.Drain(new int[16], new int[16], new long[16], new long[16], new int[2], 16);

        n.Should().Be(2);
    }
}
