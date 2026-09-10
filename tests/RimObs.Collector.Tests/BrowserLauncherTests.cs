using System.Collections.Concurrent;
using RimWorks.RimObs.Collector.Hosting;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

/// <summary>
/// A container has no browser, so the launch fails on every docker run. It must not read as a
/// crash: an expected miss logs one line, and only an unexpected failure carries a stack trace.
/// </summary>
public sealed class BrowserLauncherTests : IDisposable {
    private readonly CapturingSink _sink = new();
    private readonly ILogger _previous = Log.Logger;

    public BrowserLauncherTests() {
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
    }

    public void Dispose() {
        Log.Logger = _previous;
        Environment.SetEnvironmentVariable("BROWSER", null);
    }

    [Fact]
    public void Open_does_not_throw_when_the_browser_is_missing() {
        Environment.SetEnvironmentVariable("BROWSER", "rimobs-no-such-browser");

        Action act = () => BrowserLauncher.Open("http://127.0.0.1:25950/");

        act.Should().NotThrow();
    }

    [Fact]
    public void Open_logs_a_missing_browser_without_a_stack_trace() {
        Environment.SetEnvironmentVariable("BROWSER", "rimobs-no-such-browser");

        BrowserLauncher.Open("http://127.0.0.1:25950/");

        LogEvent evt = _sink.Events.Should().ContainSingle().Subject;
        evt.Level.Should().Be(LogEventLevel.Information);
        evt.Exception.Should().BeNull();
    }

    [Fact]
    public void Open_keeps_the_url_in_the_message_so_it_can_be_opened_by_hand() {
        Environment.SetEnvironmentVariable("BROWSER", "rimobs-no-such-browser");

        BrowserLauncher.Open("http://127.0.0.1:25950/");

        _sink.Events.Should().ContainSingle().Which
            .RenderMessage().Should().Contain("http://127.0.0.1:25950/");
    }

    private sealed class CapturingSink : ILogEventSink {
        public ConcurrentBag<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
