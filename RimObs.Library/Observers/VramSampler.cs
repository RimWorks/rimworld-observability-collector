using System.Diagnostics;
using UnityEngine;
using UProfiler = UnityEngine.Profiling.Profiler;

namespace RimWorks.RimObs.Observers;

// Unity-bound: Resources and Profiler reads need the main thread, so the frame prefix drives
// this and it stays out of unit tests. The arithmetic lives in VramCensus, which is tested.
internal static class VramSampler {
    /// <summary>Objects sized per frame. ~2k native reads is well under a tenth of a millisecond.</summary>
    private const int SliceSize = 2048;

    private static readonly long IntervalTicks = Stopwatch.Frequency * 10L;

    public static IVramSink? Sink { get; set; }

    private static long s_NextStart;
    private static int s_Phase;
    private static UnityEngine.Object[]? s_Objects;
    private static int s_Index;
    private static VramCensus? s_Census;

    /// <summary>Main thread, once per frame. Idle cost is one timestamp compare.</summary>
    public static void Pump() {
        if (s_Phase == 0 && (Sink == null || Stopwatch.GetTimestamp() < s_NextStart))
            return;
        // separate method so jitting Pump never loads UnityEngine; the net10 test host has none.
        Step();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Step() {
        // the boehm callback re-entering mono during the scan's own allocations (or this
        // method's first jit) is the historical exit-139; detach it for the slice.
        using System.IDisposable suspend = AllocationHook.SuspendScope();
        if (s_Phase == 0) {
            s_Census = new VramCensus();
            s_Objects = Resources.FindObjectsOfTypeAll<Texture>();
            s_Index = 0;
            s_Phase = 1;
            return;
        }

        UnityEngine.Object[] objects = s_Objects!;
        VramCensus census = s_Census!;
        int end = s_Index + SliceSize;
        if (end > objects.Length)
            end = objects.Length;
        for (int i = s_Index; i < end; i++) {
            UnityEngine.Object obj = objects[i];
            // unity fake-null: a destroyed object still sits in the scan array.
            if (obj == null)
                continue;
            long size = UProfiler.GetRuntimeMemorySizeLong(obj);
            byte kind = VramCensus.KindTexture;
            if (s_Phase == 2)
                kind = VramCensus.KindMesh;
            else if (obj is RenderTexture)
                kind = VramCensus.KindRenderTarget;
            census.Add(kind, size, census.Qualifies(size) ? obj.name : null);
        }
        s_Index = end;
        if (s_Index < objects.Length)
            return;

        if (s_Phase == 1) {
            s_Objects = Resources.FindObjectsOfTypeAll<Mesh>();
            s_Index = 0;
            s_Phase = 2;
            return;
        }

        Sink?.RecordVram(census.ToBatch(UProfiler.GetAllocatedMemoryForGraphicsDriver()));
        s_Objects = null;
        s_Census = null;
        s_Phase = 0;
        s_NextStart = Stopwatch.GetTimestamp() + IntervalTicks;
    }
}
