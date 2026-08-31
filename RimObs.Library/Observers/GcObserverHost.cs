using System.Collections.Generic;
using System.Threading;

namespace RimWorks.RimObs.Observers;

internal static class GcObserverHost {
    private static readonly object s_Lock = new();
    private static GcObserver? s_Instance;
    private static long s_Tick;
    private static readonly List<GcEventSample> s_RecentSamples = new(capacity: 64);
    private static IGcEventSink? s_Sink;
    private const int MaxRecentSamples = 64;
    private const int PollIntervalMs = 1000;
    private static readonly PollerThread s_Poller = new("RimObs.GcObserver", PollTick, PollIntervalMs);

    public static void SetSink(IGcEventSink? sink) {
        lock (s_Lock) {
            s_Sink = sink;
        }
    }

    public static GcObserver Instance {
        get {
            lock (s_Lock) {
                return s_Instance ??= new GcObserver();
            }
        }
    }

    public static bool IsRunning => s_Poller.IsRunning;

    public static IReadOnlyList<GcEventSample> RecentSamples {
        get {
            lock (s_Lock) {
                return s_RecentSamples.ToArray();
            }
        }
    }

    public static void Start() {
        lock (s_Lock) {
            s_Instance ??= new GcObserver();
            s_Tick = 0;
        }
        s_Poller.TryStart();
    }

    public static void Stop() => s_Poller.Stop();

    public static void ClearRecentSamples() {
        lock (s_Lock) {
            s_RecentSamples.Clear();
        }
    }

    public static bool PollOnce(long currentTick) {
        GcObserver observer = Instance;
        if (!observer.TryPoll(currentTick, out GcEventSample sample))
            return false;

        IGcEventSink? sink;
        lock (s_Lock) {
            if (s_RecentSamples.Count >= MaxRecentSamples)
                s_RecentSamples.RemoveAt(0);
            s_RecentSamples.Add(sample);
            sink = s_Sink;
        }
        sink?.RecordGcEvent(sample);
        return true;
    }

    private static void PollTick() => PollOnce(Interlocked.Increment(ref s_Tick));
}
