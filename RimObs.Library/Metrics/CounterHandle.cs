using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Metrics;

public readonly struct CounterHandle {
    public readonly int Id;

    internal CounterHandle(int id) {
        Id = id;
    }

    public bool IsValid {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Id >= 0;
    }

    public static CounterHandle Invalid => new(-1);
}
