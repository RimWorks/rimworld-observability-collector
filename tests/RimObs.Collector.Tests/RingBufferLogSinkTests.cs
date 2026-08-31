using RimWorks.RimObs.Collector.Logging;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class RingBufferLogSinkTests {
    private static readonly string[] NewestFirstMessages = ["c", "b"];
    [Fact]
    public void Ctor_rejects_zero_or_negative_capacity() {
        Action zero = () => _ = new RingBufferLogSink(0);
        Action neg = () => _ = new RingBufferLogSink(-1);
        zero.Should().Throw<ArgumentOutOfRangeException>();
        neg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Emit_appends_entries_and_Snapshot_returns_newest_first() {
        RingBufferLogSink sink = new(capacity: 10);
        ILogger logger = NewLogger(sink);

        logger.Information("first {N}", 1);
        logger.Warning("second {N}", 2);
        logger.Error("third {N}", 3);

        IReadOnlyList<LogEntry> snap = sink.Snapshot(limit: 10);
        snap.Should().HaveCount(3);
        snap[0].Message.Should().Contain("third 3");
        snap[1].Message.Should().Contain("second 2");
        snap[2].Message.Should().Contain("first 1");
    }

    [Fact]
    public void Capacity_evicts_oldest_entries() {
        RingBufferLogSink sink = new(capacity: 2);
        ILogger logger = NewLogger(sink);

        logger.Information("a");
        logger.Information("b");
        logger.Information("c");

        sink.Count.Should().Be(2);
        IReadOnlyList<LogEntry> snap = sink.Snapshot(limit: 10);
        snap.Should().HaveCount(2);
        snap.Select(e => e.Message).Should().BeEquivalentTo(NewestFirstMessages, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Snapshot_with_minLevel_filters_below_threshold() {
        RingBufferLogSink sink = new(capacity: 10);
        ILogger logger = NewLogger(sink);

        logger.Information("info-1");
        logger.Warning("warn-1");
        logger.Error("err-1");

        IReadOnlyList<LogEntry> warnPlus = sink.Snapshot(minLevel: "Warning", limit: 10);
        warnPlus.Should().HaveCount(2);
        warnPlus.Select(e => e.Level).Should().NotContain(LogEventLevel.Information);
    }

    [Fact]
    public void Snapshot_with_unparseable_level_returns_all_entries() {
        RingBufferLogSink sink = new(capacity: 10);
        ILogger logger = NewLogger(sink);
        logger.Information("a");
        logger.Error("b");

        IReadOnlyList<LogEntry> snap = sink.Snapshot(minLevel: "shenanigans", limit: 10);
        snap.Should().HaveCount(2);
    }

    [Fact]
    public void Snapshot_limit_caps_returned_entries() {
        RingBufferLogSink sink = new(capacity: 100);
        ILogger logger = NewLogger(sink);
        for (int i = 0; i < 20; i++)
            logger.Information("entry-{N}", i);

        sink.Snapshot(limit: 5).Should().HaveCount(5);
        sink.Snapshot(limit: 0).Should().BeEmpty();
    }

    [Fact]
    public void Emit_captures_exception_as_string() {
        RingBufferLogSink sink = new(capacity: 4);
        ILogger logger = NewLogger(sink);
        InvalidOperationException ex = new("boom");

        logger.Error(ex, "with exception");

        LogEntry entry = sink.Snapshot(limit: 1)[0];
        entry.Exception.Should().NotBeNull();
        entry.Exception!.Should().Contain("boom");
    }

    private static Serilog.Core.Logger NewLogger(RingBufferLogSink sink) =>
        new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Verbose)
            .WriteTo.Sink(sink)
            .CreateLogger();
}
