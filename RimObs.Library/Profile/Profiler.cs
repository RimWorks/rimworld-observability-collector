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
        public readonly long[] Tokens = new long[MaxStackDepth];
        public int Depth;
        public int Overflow;
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
        // pending sections judge from AutoMute's own accumulators; their samples were flood.
        if (sink != null && !AutoMute.IsPending(sectionId))
            sink.RecordSection(sectionId, parentId, nodeId, parentNodeId, token, elapsed, allocBytes);
    }

    public static int MaxStackDepthForTests => MaxStackDepth;

    /// <summary>
    /// Head-injection entry: the start token lives in thread state because a head and a
    /// finally injection cannot share an IL local the way the transpiler pair does.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnterById(int sectionId) {
        ThreadState state = t_State ?? InitThreadState();
        int depth = state.Depth;
        if (depth >= MaxStackDepth) {
            if (state.Overflow == 0)
                ReportPinnedStack(state);
            state.Overflow++;
            return;
        }

        // an inactive section still pushes a sentinel: a skipped push lets its Exit eat a
        // deeper frame's Overflow credit, and the judge can flip a section mid-scope.
        if (!Enabled
            || (uint)sectionId >= (uint)SectionRegistry.MaxSections
            || !SectionRegistry.s_Active[sectionId]) {
            state.Sections[depth] = sectionId;
            state.Tokens[depth] = DisabledToken;
            state.Depth = depth + 1;
            return;
        }

        state.Sections[depth] = sectionId;
        // past the soft cap the frame is still pushed: the same id can sit right below it, and a
        // skipped push there would let this Exit pop the measured frame instead.
        if (depth < s_MaxDepth) {
            int nodeId = (state.NextNodeId + 1) & NodeIdCounterMask;
            state.NextNodeId = nodeId;
            state.Nodes[depth] = state.ThreadBlock | nodeId;
            state.Allocs[depth] = AllocationHook.t_Bytes;
            state.Tokens[depth] = SpinClock.Timestamp();
        }
        else {
            state.Tokens[depth] = DisabledToken;
        }
        state.Depth = depth + 1;
    }

    /// <summary>
    /// Frame-boundary self-heal. Real depth at a frame start is 1-2; a stack at the hard cap
    /// is leaked frames, and without this one leak silences the thread for the session.
    /// </summary>
    public static void HealPinnedAtFrameBoundary() {
        ThreadState? state = t_State;
        if (state is null || state.Depth < MaxStackDepth)
            return;
        state.Depth = 0;
        state.Overflow = 0;
        RimWorks.RimLogging.Log.ErrorTo(
            Logging.LogChannels.Sections,
            "profiler stack healed at frame boundary, an enter/exit pair is leaking",
            null);
    }

    // fires once per thread, off the recording path: a pinned stack means an enter leaked
    // somewhere, and the repeated ids on it name the section whose exit never ran.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportPinnedStack(ThreadState state) {
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < MaxStackDepth; i++) {
            counts.TryGetValue(state.Sections[i], out int n);
            counts[state.Sections[i]] = n + 1;
        }
        var top = new List<string>();
        foreach (KeyValuePair<int, int> pair in counts) {
            if (pair.Value >= 4)
                top.Add($"{SectionRegistry.GetName(pair.Key)} x{pair.Value}");
        }
        RimWorks.RimLogging.Log.ErrorTo(
            Logging.LogChannels.Sections,
            "profiler depth pinned on thread {Thread}: {Stack}",
            new object?[] { Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString(), string.Join(", ", top) });
    }

    /// <summary>Finally-injection exit. Pops only its own frame, so a missed Enter is survivable.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ExitById(int sectionId) {
        ThreadState? state = t_State;
        if (state is null)
            return;
        if (state.Overflow > 0) {
            state.Overflow--;
            return;
        }
        int depth = state.Depth;
        if (depth == 0 || state.Sections[depth - 1] != sectionId)
            return;

        depth--;
        state.Depth = depth;
        long token = state.Tokens[depth];
        if (token == DisabledToken)
            return;

        long elapsed = SpinClock.Timestamp() - token;
        if (elapsed < 0)
            elapsed = 0;
        int parentId = depth > 0 ? state.Sections[depth - 1] : NoParent;
        int parentNodeId = depth > 0 ? state.Nodes[depth - 1] : NoParent;
        long allocBytes = AllocationHook.t_Bytes - state.Allocs[depth];

        if (AutoMute.Armed)
            FoldSelfTime(state, sectionId, depth, elapsed);

        ISampleSink? sink = Sink;
        if (sink != null && !AutoMute.IsPending(sectionId))
            sink.RecordSection(sectionId, parentId, state.Nodes[depth], parentNodeId, token, elapsed, allocBytes);
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
