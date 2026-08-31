using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class BoundedSampleQueueTests {
    [Fact]
    public void TryEnqueue_returns_true_while_capacity_remains() {
        BoundedSampleQueue<int> queue = new(capacity: 3);

        queue.TryEnqueue(1).Should().BeTrue();
        queue.TryEnqueue(2).Should().BeTrue();
        queue.TryEnqueue(3).Should().BeTrue();
        queue.Dropped.Should().Be(0);
    }

    [Fact]
    public void TryEnqueue_returns_false_and_increments_dropped_when_full() {
        BoundedSampleQueue<int> queue = new(capacity: 2);
        queue.TryEnqueue(1).Should().BeTrue();
        queue.TryEnqueue(2).Should().BeTrue();

        queue.TryEnqueue(3).Should().BeFalse();
        queue.TryEnqueue(4).Should().BeFalse();

        queue.Dropped.Should().Be(2);
    }

    [Fact]
    public void DrainSnapshot_frees_capacity_so_subsequent_enqueue_succeeds() {
        BoundedSampleQueue<int> queue = new(capacity: 2);
        queue.TryEnqueue(1);
        queue.TryEnqueue(2);
        queue.TryEnqueue(3).Should().BeFalse();

        int[] dest = new int[2];
        queue.DrainSnapshot(dest).Should().Be(2);

        queue.TryEnqueue(4).Should().BeTrue();
        queue.Dropped.Should().Be(1);
    }
}
