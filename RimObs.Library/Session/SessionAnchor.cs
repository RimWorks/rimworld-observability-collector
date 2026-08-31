using System;
using System.Diagnostics;

namespace RimWorks.RimObs.Session;

internal static class SessionAnchor {
    public static DateTime StartedUtc { get; private set; }
    public static long AnchorTimestamp { get; private set; }
    public static long StopwatchFrequency { get; } = Stopwatch.Frequency;
    public static string SessionId { get; private set; } = string.Empty;
    public static bool IsInitialized { get; private set; }

    public static void Initialize(string sessionId) {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session id must not be empty.", nameof(sessionId));
        if (IsInitialized)
            return;

        StartedUtc = DateTime.UtcNow;
        AnchorTimestamp = Stopwatch.GetTimestamp();
        SessionId = sessionId;
        IsInitialized = true;
    }
}
