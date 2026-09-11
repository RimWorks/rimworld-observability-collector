using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using RimWorks.RimObs.Observers;

namespace RimWorks.RimObs.Profile;

public static class Profiler {
    public const long DisabledToken = -1L;

    internal const int MaxStackDepth = 64;
    public const int NoParent = -1;

    private const int NodeIdCounterMask = 0x00FFFFFF;

    public static volatile bool Enabled = true;

    /// <summary>Nesting depth past which a section is not recorded. Collector config owns it.</summary>
    public const int DefaultMaxDepth = 8;

    private static volatile int s_MaxDepth = DefaultMaxDepth;

    public static int MaxDepth {
        get => s_MaxDepth;
        set {
            int clamped = value < 1 ? 1 : value;
            s_MaxDepth = clamped > MaxStackDepth ? MaxStackDepth : clamped;
        }
    }

    private static ISampleSink? Sink;

    private static int s_NextThreadBlock;

    // one [ThreadStatic] read per Start/Stop instead of seven; mono pays ~1.6 ns per read.
    private sealed class ThreadState {
        public readonly int[] Sections = new int[MaxStackDepth];
        public readonly int[] Nodes = new int[MaxStackDepth];
        public readonly long[] Allocs = new long[MaxStackDepth];
        public readonly long[] ChildTicks = new long[MaxStackDepth];
        public int Depth;
        public int ThreadBlock;
        public int NextNodeId;
    }

    [ThreadStatic]
    private static ThreadState? t_State;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ThreadState InitThreadState() {
        ThreadState state = new() {
            ThreadBlock = (Interlocked.Increment(ref s_NextThreadBlock) & 0xFF) << 24,
        };
        t_State = state;
        return state;
    }

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

        ThreadState state = t_State ?? InitThreadState();

        // not pushing is what keeps the pair balanced: Stop sees DisabledToken and pops nothing.
        int depth = state.Depth;
        if (depth >= s_MaxDepth)
            return DisabledToken;

        if (depth < MaxStackDepth) {
            state.Sections[depth] = sectionId;
            int nodeId = (state.NextNodeId + 1) & NodeIdCounterMask;
            state.NextNodeId = nodeId;
            state.Nodes[depth] = state.ThreadBlock | nodeId;
            state.Allocs[depth] = AllocationHook.t_Bytes;
        }
        state.Depth = depth + 1;

        return SpinClock.Timestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StopById(int sectionId, long token) {
        if (token == DisabledToken)
            return;

        // a scope can straddle the spin clock turning on or off; the domains skew by spin lag.
        long elapsed = SpinClock.Timestamp() - token;
        if (elapsed < 0)
            elapsed = 0;

        ThreadState? state = t_State;
        int depth = 0;
        int parentId = NoParent;
        int nodeId = NoParent;
        int parentNodeId = NoParent;
        long allocBytes = 0L;
        if (state != null && state.Depth > 0) {
            depth = state.Depth - 1;
            state.Depth = depth;
            if (depth < MaxStackDepth) {
                nodeId = state.Nodes[depth];
                allocBytes = AllocationHook.t_Bytes - state.Allocs[depth];
                if (depth > 0) {
                    parentId = state.Sections[depth - 1];
                    parentNodeId = state.Nodes[depth - 1];
                }
            }
        }

        if (AutoMute.Armed && state != null)
            FoldSelfTime(state, sectionId, depth, elapsed);

        ISampleSink? sink = Sink;
        if (sink != null)
            sink.RecordSection(sectionId, parentId, nodeId, parentNodeId, token, elapsed, allocBytes);
    }

    // self time needs the children's total, which only auto-mute wants. arming is one-way, so
    // the accumulators can never be read stale.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FoldSelfTime(ThreadState state, int sectionId, int depth, long elapsed) {
        long[] child = state.ChildTicks;
        if ((uint)depth >= (uint)MaxStackDepth)
            return;

        long self = elapsed - child[depth];
        child[depth] = 0;
        if (depth > 0)
            child[depth - 1] += elapsed;

        AutoMute.Observe(sectionId, self);
    }
}
