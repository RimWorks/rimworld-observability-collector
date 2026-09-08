using System;
using System.Runtime.InteropServices;

namespace RimWorks.RimObs.Observers;

// under Boehm, GC.GetTotalMemory counts freed-but-not-reused blocks as used, so churn barely
// moves it. GC_get_total_bytes is Boehm's own monotonic counter and reads it exactly.
internal static class AllocatedBytesReader {
    // a system-wide Mono install can make the SONAME resolve under CoreCLR too, from an
    // unrelated GC that always reports 0. only trust the probe when actually on Mono.
    private static readonly bool s_IsMono = Type.GetType("Mono.Runtime") != null;

    private static readonly NativeCounterProbe s_Probe = new(
        () => (long)(ulong)NativeMethods.GetTotalBytesLinux(),
        () => (long)(ulong)NativeMethods.GetTotalBytesWindows(),
        () => (long)(ulong)NativeMethods.GetTotalBytesMacOs(),
        () => (long)(ulong)NativeMethods.GetTotalBytesInternal());

    public static long GetTotalBytes() =>
        s_IsMono && s_Probe.TryRead(out long bytes) ? bytes : GC.GetTotalMemory(forceFullCollection: false);

    private static class NativeMethods {
        [DllImport("libmonoboehm-2.0.so.1", EntryPoint = "GC_get_total_bytes")]
        public static extern UIntPtr GetTotalBytesLinux();

        [DllImport("mono-2.0-bdwgc", EntryPoint = "GC_get_total_bytes")]
        public static extern UIntPtr GetTotalBytesWindows();

        [DllImport("libmonobdwgc-2.0", EntryPoint = "GC_get_total_bytes")]
        public static extern UIntPtr GetTotalBytesMacOs();

        [DllImport("__Internal", EntryPoint = "GC_get_total_bytes")]
        public static extern UIntPtr GetTotalBytesInternal();
    }
}
