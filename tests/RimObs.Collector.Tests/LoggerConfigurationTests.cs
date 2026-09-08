using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Logging;
using Serilog;
using Serilog.Events;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

/// <summary>
/// The ring buffer holds 1024 entries and the dashboard polls several endpoints a second, so
/// unfiltered request logging evicts a real error long before anyone can read it.
/// </summary>
public sealed class LoggerConfigurationTests {
    // A logger of its own, never Log.Logger: the global is shared with every other test class.
    private static (ILogger Logger, RingBufferLogSink Sink) Build() {
        RingBufferLogSink sink = new RingBufferLogSink();
        return (Program.BuildLoggerConfiguration(sink, null).CreateLogger(), sink);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    public void Aspnet_request_logging_stays_out_of_the_ring_buffer(string sourceContext) {
        (ILogger logger, RingBufferLogSink sink) = Build();

        logger.ForContext("SourceContext", sourceContext)
            .Information("Request starting HTTP/1.1 GET /api/v1/status");

        sink.Count.Should().Be(0);
    }

    [Fact]
    public void An_aspnet_warning_still_reaches_the_ring_buffer() {
        (ILogger logger, RingBufferLogSink sink) = Build();

        logger.ForContext("SourceContext", "Microsoft.AspNetCore.Server.Kestrel")
            .Warning("connection reset");

        sink.Count.Should().Be(1);
    }

    [Fact]
    public void The_collectors_own_information_logging_is_untouched() {
        (ILogger logger, RingBufferLogSink sink) = Build();

        logger.ForContext("SourceContext", "RimWorks.RimObs.Collector.Receive.UdpReceiver")
            .Information("bound to 127.0.0.1:25950");

        sink.Count.Should().Be(1);
    }
}
