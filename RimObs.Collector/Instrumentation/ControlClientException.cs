namespace RimWorks.RimObs.Collector.Instrumentation;

public sealed class ControlClientException : Exception {
    public ControlClientException(int status, string? reason = null)
        : base(reason is null
            ? $"control endpoint returned {status}"
            : $"control endpoint returned {status}: {reason}") {
        Status = status;
        Reason = reason;
    }

    public int Status { get; }

    public string? Reason { get; }
}
