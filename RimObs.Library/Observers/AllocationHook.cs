using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RimWorks.RimObs.Observers;

/// <summary>
/// Per-thread allocated bytes, read from Mono's gc_allocation profiler callback. RimWorld ships
/// Boehm, where GC.GetAllocatedBytesForCurrentThread is a hardcoded 0, so this is the only source.
/// </summary>
internal static class AllocationHook {
    public const int Off = 0;
    public const int On = 1;
    public const int Unavailable = 2;

    /// <summary>Bytes this thread has allocated since the hook was enabled.</summary>
    [ThreadStatic]
    internal static long t_Bytes;

    /// <summary>Objects this thread has allocated since the hook was enabled.</summary>
    [ThreadStatic]
    internal static long t_Count;

    internal delegate uint ObjectSizeFn(IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GcAllocationCallback(IntPtr prof, IntPtr obj);

    // mono keeps the raw pointer, so both of these have to outlive the call that installs them.
    private static GcAllocationCallback? s_Callback;
    private static ObjectSizeFn? s_Size;
    private static int s_State;

    public static int State => s_State;

    public static bool IsOn => s_State == On;

    /// <summary>The library name that resolved, for the bootstrap log. Empty until enabled.</summary>
    public static string Runtime { get; private set; } = string.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Accumulate(long size) {
        t_Bytes += size;
        t_Count++;
    }

    /// <summary>
    /// One way and one shot. Mono raises gc_allocation for the rest of the process once
    /// enabled, so a failed or repeated attempt must not retry.
    /// </summary>
    public static bool TryEnable() {
        if (s_State != Off)
            return s_State == On;

        for (int i = 0; i < s_Candidates.Length; i++) {
            Candidate candidate = s_Candidates[i];
            IntPtr handle;
            try {
                handle = candidate.Create(IntPtr.Zero);
            }
            catch (DllNotFoundException) {
                continue;
            }
            catch (EntryPointNotFoundException) {
                continue;
            }

            if (handle == IntPtr.Zero || candidate.Enable() == 0) {
                s_State = Unavailable;
                return false;
            }

            s_Size = candidate.Size;
            s_Callback = OnAllocation;
            candidate.SetCallback(handle, Marshal.GetFunctionPointerForDelegate(s_Callback));
            Runtime = candidate.Library;
            s_State = On;
            return true;
        }

        s_State = Unavailable;
        return false;
    }

    private static void OnAllocation(IntPtr prof, IntPtr obj) {
        try {
            Accumulate(s_Size!(obj));
        }
        catch (Exception) {
            // this runs inside Boehm's allocator. an exception crossing back into it kills
            // the process, so there is nothing to do but swallow it.
        }
    }

    private readonly struct Candidate {
        public Candidate(
            string library,
            Func<IntPtr, IntPtr> create,
            Func<int> enable,
            Action<IntPtr, IntPtr> setCallback,
            ObjectSizeFn size) {
            Library = library;
            Create = create;
            Enable = enable;
            SetCallback = setCallback;
            Size = size;
        }

        public string Library { get; }
        public Func<IntPtr, IntPtr> Create { get; }
        public Func<int> Enable { get; }
        public Action<IntPtr, IntPtr> SetCallback { get; }
        public ObjectSizeFn Size { get; }
    }

    // DllImport needs a compile-time constant, so each runtime name gets its own block. Only
    // the linux SONAME is verified in-game; the rest are the names Unity ships elsewhere.
    private static readonly Candidate[] s_Candidates = [
        new Candidate("libmonoboehm-2.0.so.1", LinuxSoname.mono_profiler_create, LinuxSoname.mono_profiler_enable_allocations, LinuxSoname.mono_profiler_set_gc_allocation_callback, LinuxSoname.mono_object_get_size),
        new Candidate("libmonobdwgc-2.0", Bdwgc.mono_profiler_create, Bdwgc.mono_profiler_enable_allocations, Bdwgc.mono_profiler_set_gc_allocation_callback, Bdwgc.mono_object_get_size),
        new Candidate("mono-2.0-bdwgc", MonoBdwgc.mono_profiler_create, MonoBdwgc.mono_profiler_enable_allocations, MonoBdwgc.mono_profiler_set_gc_allocation_callback, MonoBdwgc.mono_object_get_size),
        new Candidate("__Internal", Internal.mono_profiler_create, Internal.mono_profiler_enable_allocations, Internal.mono_profiler_set_gc_allocation_callback, Internal.mono_object_get_size),
    ];

    private static class LinuxSoname {
        private const string Lib = "libmonoboehm-2.0.so.1";

        [DllImport(Lib)]
        internal static extern IntPtr mono_profiler_create(IntPtr prof);

        [DllImport(Lib)]
        internal static extern int mono_profiler_enable_allocations();

        [DllImport(Lib)]
        internal static extern void mono_profiler_set_gc_allocation_callback(IntPtr handle, IntPtr cb);

        [DllImport(Lib)]
        internal static extern uint mono_object_get_size(IntPtr obj);
    }

    private static class Bdwgc {
        private const string Lib = "libmonobdwgc-2.0";

        [DllImport(Lib)]
        internal static extern IntPtr mono_profiler_create(IntPtr prof);

        [DllImport(Lib)]
        internal static extern int mono_profiler_enable_allocations();

        [DllImport(Lib)]
        internal static extern void mono_profiler_set_gc_allocation_callback(IntPtr handle, IntPtr cb);

        [DllImport(Lib)]
        internal static extern uint mono_object_get_size(IntPtr obj);
    }

    private static class MonoBdwgc {
        private const string Lib = "mono-2.0-bdwgc";

        [DllImport(Lib)]
        internal static extern IntPtr mono_profiler_create(IntPtr prof);

        [DllImport(Lib)]
        internal static extern int mono_profiler_enable_allocations();

        [DllImport(Lib)]
        internal static extern void mono_profiler_set_gc_allocation_callback(IntPtr handle, IntPtr cb);

        [DllImport(Lib)]
        internal static extern uint mono_object_get_size(IntPtr obj);
    }

    private static class Internal {
        private const string Lib = "__Internal";

        [DllImport(Lib)]
        internal static extern IntPtr mono_profiler_create(IntPtr prof);

        [DllImport(Lib)]
        internal static extern int mono_profiler_enable_allocations();

        [DllImport(Lib)]
        internal static extern void mono_profiler_set_gc_allocation_callback(IntPtr handle, IntPtr cb);

        [DllImport(Lib)]
        internal static extern uint mono_object_get_size(IntPtr obj);
    }
}
