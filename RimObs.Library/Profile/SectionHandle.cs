using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Profile;

public readonly struct SectionHandle {
    public readonly int Id;

    internal SectionHandle(int id) {
        Id = id;
    }

    public bool IsValid {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Id >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActive() => SectionRegistry.IsActive(Id);

    public string Name => SectionRegistry.GetName(Id);

    public static SectionHandle Invalid => new(-1);
}
