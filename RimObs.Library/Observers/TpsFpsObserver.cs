using System.Diagnostics;

namespace RimWorks.RimObs.Observers;

// Derives ticks-per-second and frames-per-second by differencing the cumulative FrameTickCounters
// against wall time between polls. The tick count doubles as the sample's monotonic tick number.
internal sealed class TpsFpsObserver {
    private static readonly long s_TimestampTicksPerSecond = Stopwatch.Frequency;

    private long _lastTicks;
    private long _lastFrames;
    private long _lastTimestamp;

    public TpsFpsObserver() {
        _lastTicks = FrameTickCounters.Ticks;
        _lastFrames = FrameTickCounters.Frames;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public bool TryPoll(out TpsFpsSample sample) {
        long ticksNow = FrameTickCounters.Ticks;
        long framesNow = FrameTickCounters.Frames;
        long timestampNow = Stopwatch.GetTimestamp();

        long elapsedTimestampTicks = timestampNow - _lastTimestamp;
        if (elapsedTimestampTicks <= 0) {
            sample = default;
            return false;
        }

        double elapsedSeconds = (double)elapsedTimestampTicks / s_TimestampTicksPerSecond;
        double tps = (ticksNow - _lastTicks) / elapsedSeconds;
        double fps = (framesNow - _lastFrames) / elapsedSeconds;

        _lastTicks = ticksNow;
        _lastFrames = framesNow;
        _lastTimestamp = timestampNow;

        sample = new TpsFpsSample(tps, fps, ticksNow);
        return true;
    }
}
