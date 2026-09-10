using System.Runtime.CompilerServices;

namespace RimObsTest.AutoFixtures;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static class AutoTargets {
    public static int Sink;

    public static int Trivial() => 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Worthwhile(int n) {
        int acc = 0;
        for (int i = 0; i < n; i++)
            acc += i * Sink + acc;
        return acc;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AlsoWorthwhile(int n) {
        int acc = 1;
        for (int i = 0; i < n; i++)
            acc += i + Sink - acc;
        return acc;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ThirdWorthwhile(int n) {
        int acc = 2;
        for (int i = 0; i < n; i++)
            acc += i - Sink + acc;
        return acc;
    }
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static class OtherAutoTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Elsewhere(int n) {
        int acc = 3;
        for (int i = 0; i < n; i++)
            acc += i * 3 + acc;
        return acc;
    }
}

/// <summary>
/// A struct with both shapes, so the scan can prove it refuses the instance method and keeps
/// the static one. Mirrors Gilzoide.ManagedJobs.ManagedJob, which crashed the game.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public struct StructScanTargets {
    private readonly int _value;

    public StructScanTargets(int value) => _value = value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int InstanceWork(int n) {
        int acc = _value;
        for (int i = 0; i < n; i++)
            acc += i * n + acc;
        return acc;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StaticWork(int n) {
        int acc = 0;
        for (int i = 0; i < n; i++)
            acc += i * n + acc;
        return acc;
    }
}
