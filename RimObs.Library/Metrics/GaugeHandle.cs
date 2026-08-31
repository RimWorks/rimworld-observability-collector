using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Metrics;

public readonly struct GaugeHandle {
    public readonly int Id;

    internal GaugeHandle(int id) {
        Id = id;
    }

    public bool IsValid {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Id >= 0;
    }

    public static GaugeHandle Invalid => new(-1);
}
