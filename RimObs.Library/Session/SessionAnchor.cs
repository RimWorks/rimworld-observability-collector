using System;
using System.Diagnostics;

namespace RimWorks.RimObs.Session;

internal static class SessionAnchor {
    public static DateTime StartedUtc { get; private set; }
    public static long AnchorTimestamp { get; private set; }
    public static long StopwatchFrequency { get; } = Stopwatch.Frequency;
    public static string SessionId { get; private set; } = string.Empty;
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Re-anchors onto a new session id. Unlike <see cref="Initialize"/> this deliberately
    /// overwrites an existing anchor, because a new session is a new clock as well as a new id:
    /// frame timestamps are relative to the anchor, so keeping the old one would date every
    /// sample in the new session to the previous run.
    /// </summary>
    public static void Restart(string sessionId) {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session id must not be empty.", nameof(sessionId));

        StartedUtc = DateTime.UtcNow;
        AnchorTimestamp = Stopwatch.GetTimestamp();
        SessionId = sessionId;
        IsInitialized = true;
    }

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
