namespace RimWorks.RimObs.Collector.Api;

public static class QueryLimit {
    public static int Clamp(int? requested, int defaultLimit, int maxLimit) {
        if (requested is not int value || value <= 0)
            return defaultLimit;
        return Math.Min(value, maxLimit);
    }
}
