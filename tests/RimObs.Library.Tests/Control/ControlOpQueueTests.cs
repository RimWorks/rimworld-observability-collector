using System;
using System.Collections.Generic;
using System.Threading;
using RimWorks.RimObs.Library.Control;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Control;

public class ControlOpQueueTests {
    [Fact]
    public void Drain_executes_each_enqueued_op_once_in_FIFO_order() {
        List<int> executed = new List<int>();
        ControlOpQueue queue = new ControlOpQueue();

        queue.Enqueue(new ControlOp(ControlOpKind.Patch, () => executed.Add(1)));
        queue.Enqueue(new ControlOp(ControlOpKind.Unpatch, () => executed.Add(2)));

        queue.Drain();

        executed.Should().Equal(1, 2);
    }

    [Fact]
    public void Drain_does_not_re_execute_processed_ops() {
        int count = 0;
        ControlOpQueue queue = new ControlOpQueue();
        queue.Enqueue(new ControlOp(ControlOpKind.Patch, () => count++));

        queue.Drain();
        queue.Drain();

        count.Should().Be(1);
    }

    [Fact]
    public void Enqueue_signal_releases_blocked_waiter() {
        ControlOpQueue queue = new ControlOpQueue();
        ControlOp op = new ControlOp(ControlOpKind.Patch, () => Thread.Sleep(10));
        queue.Enqueue(op);

        // Drain on this thread, then verify wait completes.
        queue.Drain();
        bool ok = op.Wait(TimeSpan.FromSeconds(1));
        ok.Should().BeTrue();
    }
}
