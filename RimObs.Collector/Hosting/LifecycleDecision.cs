namespace RimWorks.RimObs.Collector.Hosting;

public static class LifecycleDecision {
    public static bool ShouldShutdown(bool parentTracked, bool parentAlive, TimeSpan sinceLastActivity, TimeSpan idleTimeout) {
        if (!parentTracked)
            return false;
        if (!parentAlive)
            return true;
        if (idleTimeout > TimeSpan.Zero && sinceLastActivity >= idleTimeout)
            return true;
        return false;
    }
}
