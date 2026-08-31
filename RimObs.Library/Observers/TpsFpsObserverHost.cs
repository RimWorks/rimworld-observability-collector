using System.Threading;

namespace RimWorks.RimObs.Observers;

internal static class TpsFpsObserverHost {
    private static readonly object s_Lock = new();
    private static TpsFpsObserver? s_Instance;
    private static ITpsFpsSink? s_Sink;
    private static TpsFpsSample s_Latest;
    private static bool s_HasLatest;
    private const int PollIntervalMs = 1000;
    private static readonly PollerThread s_Poller = new("RimObs.TpsFpsObserver", PollTick, PollIntervalMs);

    public static void SetSink(ITpsFpsSink? sink) {
        lock (s_Lock) {
            s_Sink = sink;
        }
    }

    public static TpsFpsObserver Instance {
        get {
            lock (s_Lock) {
                return s_Instance ??= new TpsFpsObserver();
            }
        }
    }

    public static bool IsRunning => s_Poller.IsRunning;

    public static bool TryGetLatest(out TpsFpsSample sample) {
        lock (s_Lock) {
            sample = s_Latest;
            return s_HasLatest;
        }
    }

    public static void Start() {
        lock (s_Lock) {
            s_Instance ??= new TpsFpsObserver();
            s_HasLatest = false;
        }
        s_Poller.TryStart();
    }

    public static void Stop() => s_Poller.Stop();

    public static bool PollOnce() {
        TpsFpsObserver observer = Instance;
        if (!observer.TryPoll(out TpsFpsSample sample))
            return false;

        ITpsFpsSink? sink;
        lock (s_Lock) {
            s_Latest = sample;
            s_HasLatest = true;
            sink = s_Sink;
        }
        sink?.RecordTpsFps(sample);
        return true;
    }

    private static void PollTick() => PollOnce();
}
