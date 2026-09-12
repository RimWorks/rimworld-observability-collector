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
    private static volatile int s_FrameOrdinal;
    private static volatile int s_LastGcFrameOrdinal;
    private static int s_LastCollectionCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordTick() => Interlocked.Increment(ref s_Ticks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordFrame() => Interlocked.Increment(ref s_Frames);

    // separate from s_Frames because the sample path reads this once per sample, and
    // Interlocked.Read on a long is a lock-prefixed cmpxchg on net472.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginFrame() => s_FrameOrdinal++;

    public static int FrameOrdinal => s_FrameOrdinal;

    // the gc poller only wakes once a second, so it cannot say which frame a collection landed
    // in. this is sampled every frame instead, which is the only place that can.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NoteCollections(int collectionCount) {
        if (collectionCount == s_LastCollectionCount)
            return;
        s_LastCollectionCount = collectionCount;
        // this runs after BeginFrame bumped the ordinal, and the pause happened before that
        // bump, so the frame that actually stalled is the previous one.
        s_LastGcFrameOrdinal = s_FrameOrdinal > 0 ? s_FrameOrdinal - 1 : 0;
    }

    public static int LastGcFrameOrdinal => s_LastGcFrameOrdinal;

    public static long Ticks => Interlocked.Read(ref s_Ticks);

    public static long Frames => Interlocked.Read(ref s_Frames);

    public static void Reset() {
        Interlocked.Exchange(ref s_Ticks, 0);
        Interlocked.Exchange(ref s_Frames, 0);
        s_FrameOrdinal = 0;
        s_LastGcFrameOrdinal = 0;
        s_LastCollectionCount = 0;
    }
}
