namespace RimWorks.RimObs.Observers;

internal readonly struct TpsFpsSample {
    public TpsFpsSample(double tps, double fps, long tick) {
        Tps = tps;
        Fps = fps;
        Tick = tick;
    }

    public double Tps { get; }
    public double Fps { get; }
    public long Tick { get; }
}
