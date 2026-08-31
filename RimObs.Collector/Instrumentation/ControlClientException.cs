namespace RimWorks.RimObs.Collector.Instrumentation;

public sealed class ControlClientException : Exception {
    public ControlClientException(int status) : base($"control endpoint returned {status}") {
        Status = status;
    }

    public int Status { get; }
}
