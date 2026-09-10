using System;

namespace RimWorks.RimObs.Auto;

/// <summary>
/// The latest auto-instrumentation settings the collector sent, waiting for a main-thread pickup.
/// The config poll runs on its own thread and a scan touches Harmony's registry, so the poll
/// parks the request here and <see cref="AutoInstrumentRunner.Pump"/> acts on it in a frame.
/// </summary>
internal static class AutoInstrumentRequest {
    private static readonly object s_Gate = new object();

    private static bool s_Seen;
    private static bool s_Enabled;
    private static string s_Filters = string.Empty;
    private static string s_Ignore = string.Empty;
    private static bool s_MuteTrivial = true;

    private static volatile bool s_Pending;

    public static bool HasPending => s_Pending;

    /// <summary>Records a settings snapshot. A snapshot equal to the last one is dropped.</summary>
    public static void Set(bool enabled, string? filters, string? ignore, bool muteTrivial) {
        string effective = enabled ? filters ?? string.Empty : string.Empty;
        string ignored = ignore ?? string.Empty;

        lock (s_Gate) {
            if (s_Seen
                && enabled == s_Enabled
                && string.Equals(effective, s_Filters, StringComparison.Ordinal)
                && string.Equals(ignored, s_Ignore, StringComparison.Ordinal)
                && muteTrivial == s_MuteTrivial) {
                return;
            }

            s_Seen = true;
            s_Enabled = enabled;
            s_Filters = effective;
            s_Ignore = ignored;
            s_MuteTrivial = muteTrivial;
            s_Pending = true;
        }
    }

    public static bool TryTake(out bool enabled, out string filters, out string ignore, out bool muteTrivial) {
        if (!s_Pending) {
            enabled = false;
            filters = string.Empty;
            ignore = string.Empty;
            muteTrivial = true;
            return false;
        }

        lock (s_Gate) {
            enabled = s_Enabled;
            filters = s_Filters;
            ignore = s_Ignore;
            muteTrivial = s_MuteTrivial;
            s_Pending = false;
            return true;
        }
    }

    internal static void ResetForTests() {
        lock (s_Gate) {
            s_Seen = false;
            s_Enabled = false;
            s_Filters = string.Empty;
            s_Ignore = string.Empty;
            s_MuteTrivial = true;
            s_Pending = false;
        }
    }
}
