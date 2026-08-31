using System;
using System.Runtime.CompilerServices;
using RimWorks.RimObs.Profile;

namespace RimWorks.RimObs.Api;

public readonly struct MeasureScope : IDisposable {
    private readonly int _sectionId;
    private readonly long _token;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MeasureScope(int sectionId, long token) {
        _sectionId = sectionId;
        _token = token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {
        Profiler.StopById(_sectionId, _token);
    }
}
