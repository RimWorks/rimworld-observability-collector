using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RimWorks.RimObs.Profile;

public static class Profiler {
    public const long DisabledToken = -1L;

    internal const int MaxStackDepth = 64;
    public const int NoParent = -1;

    public static volatile bool Enabled = true;

    private static ISampleSink? Sink;

    [ThreadStatic]
    private static int[]? s_Stack;

    [ThreadStatic]
    private static int s_Depth;

    internal static void SetSink(ISampleSink? sink) => Sink = sink;

    internal static void SetEnabled(bool enabled) => Enabled = enabled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Start(SectionHandle handle) => StartById(handle.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Stop(SectionHandle handle, long token) => StopById(handle.Id, token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long StartById(int sectionId) {
        if (!Enabled)
            return DisabledToken;
        if ((uint)sectionId >= (uint)SectionRegistry.MaxSections)
            return DisabledToken;
        if (!SectionRegistry.s_Active[sectionId])
            return DisabledToken;

        int[] stack = s_Stack ??= new int[MaxStackDepth];
        int depth = s_Depth;
        if (depth < MaxStackDepth)
            stack[depth] = sectionId;
        s_Depth = depth + 1;

        return Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StopById(int sectionId, long token) {
        if (token == DisabledToken)
            return;

        long elapsed = Stopwatch.GetTimestamp() - token;

        int depth = s_Depth;
        int parentId = NoParent;
        if (depth > 0) {
            depth--;
            s_Depth = depth;
            int[]? stack = s_Stack;
            if (stack != null && depth > 0 && depth - 1 < MaxStackDepth)
                parentId = stack[depth - 1];
        }

        ISampleSink? sink = Sink;
        if (sink != null)
            sink.RecordSection(sectionId, parentId, token, elapsed);
    }
}
