using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Metrics;

public readonly struct HistogramHandle {
    public readonly int Id;

    internal HistogramHandle(int id) {
        Id = id;
    }

    public bool IsValid {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Id >= 0;
    }

    public static HistogramHandle Invalid => new(-1);
}
