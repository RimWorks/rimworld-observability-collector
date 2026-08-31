using System.Collections.Concurrent;

namespace RimWorks.RimObs.Library.Control;

internal sealed class ControlOpQueue {
    private readonly ConcurrentQueue<ControlOp> _ops = new ConcurrentQueue<ControlOp>();

    public void Enqueue(ControlOp op) {
        _ops.Enqueue(op);
    }

    public void Drain() {
        while (_ops.TryDequeue(out ControlOp? op))
            op!.Execute();
    }

    public int PendingCount => _ops.Count;
}
