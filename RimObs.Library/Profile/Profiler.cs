using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RimWorks.RimObs.Profile;

public static class Profiler {
    public const long DisabledToken = -1L;

    internal const int MaxStackDepth = 64;
    public const int NoParent = -1;

    private const int NodeIdCounterMask = 0x00FFFFFF;

    public static volatile bool Enabled = true;

    private static ISampleSink? Sink;

    private static int s_NextThreadBlock;

    [ThreadStatic]
    private static int[]? s_Stack;

    [ThreadStatic]
    private static int s_Depth;

    [ThreadStatic]
    private static int[]? s_NodeStack;

    [ThreadStatic]
    private static int s_ThreadBlock;

    [ThreadStatic]
    private static int s_NextNodeId;

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

        int[]? nodes = s_NodeStack;
        if (nodes == null) {
            nodes = s_NodeStack = new int[MaxStackDepth];
            s_ThreadBlock = (Interlocked.Increment(ref s_NextThreadBlock) & 0xFF) << 24;
        }
        if (depth < MaxStackDepth) {
            s_NextNodeId = (s_NextNodeId + 1) & NodeIdCounterMask;
            nodes[depth] = s_ThreadBlock | s_NextNodeId;
        }

        return Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StopById(int sectionId, long token) {
        if (token == DisabledToken)
            return;

        long elapsed = Stopwatch.GetTimestamp() - token;

        int depth = s_Depth;
        int parentId = NoParent;
        int nodeId = NoParent;
        int parentNodeId = NoParent;
        if (depth > 0) {
            depth--;
            s_Depth = depth;
            int[]? stack = s_Stack;
            if (stack != null && depth > 0 && depth - 1 < MaxStackDepth)
                parentId = stack[depth - 1];

            int[]? nodes = s_NodeStack;
            if (nodes != null && depth < MaxStackDepth) {
                nodeId = nodes[depth];
                if (depth > 0)
                    parentNodeId = nodes[depth - 1];
            }
        }

        ISampleSink? sink = Sink;
        if (sink != null)
            sink.RecordSection(sectionId, parentId, nodeId, parentNodeId, token, elapsed);
    }
}
