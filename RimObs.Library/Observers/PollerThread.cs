using System;
using System.Threading;

namespace RimWorks.RimObs.Observers;

internal sealed class PollerThread {
    private readonly object _lock = new();
    private readonly string _name;
    private readonly Action _tick;
    private readonly int _intervalMs;
    private Thread? _thread;
    private volatile bool _stop;

    public PollerThread(string name, Action tick, int intervalMs) {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Thread name must not be empty.", nameof(name));
        if (tick == null)
            throw new ArgumentNullException(nameof(tick));
        if (intervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Poll interval must be positive.");
        _name = name;
        _tick = tick;
        _intervalMs = intervalMs;
    }

    public bool IsRunning {
        get {
            lock (_lock) {
                return _thread != null;
            }
        }
    }

    public void Start() => TryStart();

    public bool TryStart() {
        lock (_lock) {
            if (_thread != null)
                return false;
            _stop = false;
            _thread = new Thread(Loop) {
                Name = _name,
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
            };
            _thread.Start();
            return true;
        }
    }

    public void Stop(int joinTimeoutMs = 2000) {
        Thread? thread;
        lock (_lock) {
            thread = _thread;
            _thread = null;
            _stop = true;
        }
        thread?.Join(joinTimeoutMs);
    }

    private void Loop() {
        while (!_stop) {
            // A single throwing tick must not tear down the poll thread: that would silently
            // strip config polling / panel refresh for the rest of the session. Failures here
            // are swallowed without reporting, matching the tick implementations themselves
            // (e.g. CollectorConfigClient.Fetch returns null on transport errors and logs
            // nothing). The loop's only contract is liveness, not surfacing failures.
            try {
                _tick();
            }
            catch {
                // swallow and keep polling; see contract note above
            }
            Thread.Sleep(_intervalMs);
        }
    }
}
