using System;

namespace RimWorks.RimObs.Collector.Update;

public sealed class UpdateState {
    private readonly object _lock = new();
    private ReleaseInfo? _latest;
    private DateTime _checkedUtc;

    public ReleaseInfo? Latest {
        get {
            lock (_lock) {
                return _latest;
            }
        }
    }

    public DateTime CheckedUtc {
        get {
            lock (_lock) {
                return _checkedUtc;
            }
        }
    }

    public void Set(ReleaseInfo? release) {
        lock (_lock) {
            _latest = release;
            _checkedUtc = DateTime.UtcNow;
        }
    }
}
