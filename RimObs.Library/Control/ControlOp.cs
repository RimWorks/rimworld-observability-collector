using System.Threading;

namespace RimWorks.RimObs.Library.Control;

internal sealed class ControlOp {
    private readonly System.Action _work;
    private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

    public ControlOp(ControlOpKind kind, System.Action work) {
        Kind = kind;
        _work = work;
    }

    public ControlOpKind Kind { get; }
    public System.Exception? Error { get; private set; }

    internal void Execute() {
        try {
            _work();
        }
        catch (System.Exception ex) {
            Error = ex;
        }
        finally {
            _done.Set();
        }
    }

    public bool Wait(System.TimeSpan timeout) => _done.Wait(timeout);
}
