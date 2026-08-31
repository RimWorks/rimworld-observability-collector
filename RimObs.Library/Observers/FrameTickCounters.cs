using System.Runtime.CompilerServices;
using System.Threading;

namespace RimWorks.RimObs.Observers;

// Monotonic counters fed by the per-tick and per-frame Harmony postfixes (see FrameTickPatches).
// Postfixes run on RimWorld's main thread; the TpsFpsObserver reads them from a background poller,
// so both accesses go through Interlocked. A single atomic increment per tick/frame is the entire
// cost on the game's hot path.
internal static class FrameTickCounters {
    private static long s_Ticks;
    private static long s_Frames;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordTick() => Interlocked.Increment(ref s_Ticks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordFrame() => Interlocked.Increment(ref s_Frames);

    public static long Ticks => Interlocked.Read(ref s_Ticks);

    public static long Frames => Interlocked.Read(ref s_Frames);

    public static void Reset() {
        Interlocked.Exchange(ref s_Ticks, 0);
        Interlocked.Exchange(ref s_Frames, 0);
    }
}
