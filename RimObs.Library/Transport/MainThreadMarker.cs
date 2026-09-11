using System;
using System.Threading;

namespace RimWorks.RimObs.Transport;

/// <summary>
/// Records the real Unity main thread. RimWorld constructs mods on the threaded-loading
/// worker, so a constructor capture names the wrong thread; the frame prefix marks it from
/// inside a frame instead.
/// </summary>
internal static class MainThreadMarker {
    private static int s_MainThreadId;

    /// <summary>Call only from the main thread. One volatile read and a branch per frame.</summary>
    public static void Mark() {
        int id = Environment.CurrentManagedThreadId;
        if (Volatile.Read(ref s_MainThreadId) != id)
            Volatile.Write(ref s_MainThreadId, id);
    }

    /// <summary>Zero until the first frame has run.</summary>
    public static int MainThreadId => Volatile.Read(ref s_MainThreadId);

    internal static void ResetForTests() => Volatile.Write(ref s_MainThreadId, 0);
}
