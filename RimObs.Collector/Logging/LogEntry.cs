using Serilog.Events;

namespace RimWorks.RimObs.Collector.Logging;

public sealed record LogEntry(DateTimeOffset Timestamp, LogEventLevel Level, string Message, string? Exception);
