using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RimWorks.RimObs.Profile;

/// <summary>
/// Publishes <see cref="Stopwatch.GetTimestamp"/> from a dedicated spinner thread so the hot
/// path pays a volatile read (~2 ns) instead of the mono clock icall (~18 ns).
/// </summary>
internal static class SpinClock {
    /// <summary>Fewer cores than this and the spinner would fight the game; stay on the icall.</summary>
    public const int DefaultMinCores = 8;

    /// <summary>Sender passes with the published value this stale before the clock is distrusted.</summary>
    public const int MaxStrikes = 5;

    public static int MinCores { get; set; } = DefaultMinCores;

    private static long s_Now;
    private static volatile bool s_Spinning;
    private static volatile bool s_Disabled;
    private static Thread? s_Thread;
    private static int s_Strikes;

    internal static volatile bool s_TestFreeze;

    /// <summary>A descheduled spinner freezes the clock; past this skew a Verify pass strikes.</summary>
    public static long MaxSkewTicks { get; } = Stopwatch.Frequency / 1000;

    public static bool IsSpinning => s_Spinning;
    public static bool IsDisabled => s_Disabled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Timestamp() =>
        s_Spinning ? Volatile.Read(ref s_Now) : Stopwatch.GetTimestamp();

    public static void Start() {
        if (s_Thread != null || s_Disabled || EffectiveCores() < MinCores)
            return;

        s_Now = Stopwatch.GetTimestamp();
        s_Spinning = true;
        s_Thread = new Thread(Spin) {
            Name = "RimObs.SpinClock",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        s_Thread.Start();
    }

    public static void Stop() {
        s_Spinning = false;
        s_Thread = null;
    }

    /// <summary>Called by the sender each pass. Distrusts the spinner after sustained staleness.</summary>
    public static void Verify() {
        if (!s_Spinning)
            return;

        long skew = Stopwatch.GetTimestamp() - Volatile.Read(ref s_Now);
        if (skew <= MaxSkewTicks) {
            s_Strikes = 0;
            return;
        }

        if (++s_Strikes >= MaxStrikes) {
            s_Disabled = true;
            Stop();
        }
    }

    // ProcessorCount sees the host's cores inside a container; the cgroup quota is the real
    // budget. a 6-cpu cap on a 32-core host must read as 6 or the spinner starves the game.
    private static int EffectiveCores() {
        try {
            string path = "/sys/fs/cgroup/cpu.max";
            if (File.Exists(path)) {
                int quota = ParseCpuMax(File.ReadAllText(path));
                if (quota > 0)
                    return Math.Min(quota, Environment.ProcessorCount);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // no cgroup file or no permission: fall through to the raw core count.
        }
        return Environment.ProcessorCount;
    }

    /// <summary>Parses cgroup v2 "quota period" into whole cpus, or 0 when unlimited/garbage.</summary>
    internal static int ParseCpuMax(string text) {
        string[] parts = text.Trim().Split(' ');
        if (parts.Length != 2 || parts[0] == "max")
            return 0;
        if (!long.TryParse(parts[0], out long quota) || !long.TryParse(parts[1], out long period) || period <= 0)
            return 0;
        return (int)(quota / period);
    }

    private static void Spin() {
        while (s_Spinning) {
            if (!s_TestFreeze)
                Interlocked.Exchange(ref s_Now, Stopwatch.GetTimestamp());
        }
    }

    internal static void ResetForTests() {
        s_Spinning = false;
        s_Thread = null;
        s_Disabled = false;
        s_Strikes = 0;
        s_TestFreeze = false;
        MinCores = DefaultMinCores;
    }
}
