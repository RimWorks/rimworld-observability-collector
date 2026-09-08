using System;

namespace RimWorks.RimObs.Observers;

// tries each candidate once, in order, and caches whichever wins (or that none did) so a
// missing native library only ever throws on the first read, never on every poll.
internal sealed class NativeCounterProbe {
    private const int Unresolved = -2;
    private const int NoneAvailable = -1;

    private readonly Func<long>[] _candidates;
    private int _resolvedIndex = Unresolved;

    public NativeCounterProbe(params Func<long>[] candidates) => _candidates = candidates;

    public bool TryRead(out long value) {
        if (_resolvedIndex == Unresolved)
            return Resolve(out value);

        if (_resolvedIndex == NoneAvailable) {
            value = 0;
            return false;
        }

        try {
            value = _candidates[_resolvedIndex]();
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            // the winning candidate stopped resolving; give up on native reads for good.
        }

        _resolvedIndex = NoneAvailable;
        value = 0;
        return false;
    }

    private bool Resolve(out long value) {
        for (int i = 0; i < _candidates.Length; i++) {
            try {
                value = _candidates[i]();
                _resolvedIndex = i;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
                // library or symbol missing on this platform; try the next candidate.
            }
        }
        _resolvedIndex = NoneAvailable;
        value = 0;
        return false;
    }
}
