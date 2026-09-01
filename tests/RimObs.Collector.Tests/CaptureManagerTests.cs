using System;
using System.Diagnostics;
using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Captures;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class CaptureManagerTests {
    private static (SessionAggregator Aggregator, CaptureManager Manager, ConfigStore Config) NewFixture(
        Action<RimObsConfig>? configure = null) {
        ConfigStore config = new(configFilePath: null);
        if (configure is not null) {
            RimObsConfig next = config.Current;
            configure(next);
            config.Replace(next);
        }

        SessionAggregator aggregator = new();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "cap-test",
            StopwatchFrequency = 1_000_000,
            AnchorTimestamp = Stopwatch.GetTimestamp(),
        });
        CaptureManager manager = new(aggregator, config);
        return (aggregator, manager, config);
    }

    private static SectionBatch Batch(int[] ids, int[] parents, long[] elapsed) {
        return new SectionBatch {
            SectionIds = ids,
            ParentIds = parents,
            ElapsedTicks = elapsed,
            StartTimestamps = ids.Select((_, i) => (long)i).ToArray(),
        };
    }

    [Fact]
    public void Start_then_stop_finalizes_with_user_reason() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture();

        CaptureSession started = manager.Start(CaptureTrigger.Manual);
        started.IsRunning.Should().BeTrue();
        started.Trigger.Should().Be(CaptureTrigger.Manual);
        manager.Active.Should().BeSameAs(started);

        CaptureSession? stopped = manager.Stop();
        stopped.Should().BeSameAs(started);
        stopped!.IsRunning.Should().BeFalse();
        stopped.FinalizeReason.Should().Be(CaptureFinalizeReason.UserStopped);
        stopped.StoppedUtc.Should().NotBeNull();
        manager.Active.Should().BeNull();
    }

    [Fact]
    public void Start_is_idempotent_while_a_capture_is_running() {
        (_, CaptureManager manager, _) = NewFixture();

        CaptureSession first = manager.Start(CaptureTrigger.Manual);
        CaptureSession second = manager.Start(CaptureTrigger.Manual);

        second.Should().BeSameAs(first);
        manager.Snapshot().Should().ContainSingle();
    }

    [Fact]
    public void Stop_with_no_active_capture_returns_null() {
        (_, CaptureManager manager, _) = NewFixture();
        manager.Stop().Should().BeNull();
    }

    [Fact]
    public void Running_capture_records_call_edges_during_window() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture();

        CaptureSession capture = manager.Start(CaptureTrigger.Manual);
        aggregator.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [1, 2],
            Names = ["root", "child"],
        });
        aggregator.OnSectionBatch(Batch([1, 2, 2], [-1, 1, 1], [1000, 200, 300]));

        capture.EdgeCount.Should().Be(2);
        capture.Names.Should().ContainKey(1).WhoseValue.Should().Be("root");

        CallEdgeStats rootEdge = capture.SnapshotEdges().Single(e => e.SectionId == 1);
        rootEdge.CallCount.Should().Be(1);
        rootEdge.TotalElapsedTicks.Should().Be(1000);
    }

    [Fact]
    public void Edges_recorded_before_start_and_after_stop_are_ignored() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture();

        aggregator.OnSectionBatch(Batch([1], [-1], [500]));
        CaptureSession capture = manager.Start(CaptureTrigger.Manual);
        aggregator.OnSectionBatch(Batch([2], [-1], [500]));
        manager.Stop();
        aggregator.OnSectionBatch(Batch([3], [-1], [500]));

        capture.SnapshotEdges().Select(e => e.SectionId).Should().BeEquivalentTo([2]);
    }

    [Fact]
    public void Slow_tick_auto_starts_capture_when_enabled() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture(c => {
            c.Sampling.FocusedCaptureEnabled = true;
            // 1000 us threshold; freq 1_000_000 ticks/sec => 1000 ticks.
            c.Session.SlowTickThresholdUs = 1000;
        });

        manager.Active.Should().BeNull();
        aggregator.OnSectionBatch(Batch([1], [-1], [5000]));

        CaptureSession? active = manager.Active;
        active.Should().NotBeNull();
        active!.Trigger.Should().Be(CaptureTrigger.SlowTick);
        active.SnapshotEdges().Should().ContainSingle(e => e.SectionId == 1);
    }

    [Fact]
    public void Slow_tick_does_not_auto_start_when_disabled() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture(c => {
            c.Sampling.FocusedCaptureEnabled = false;
            c.Session.SlowTickThresholdUs = 1000;
        });

        aggregator.OnSectionBatch(Batch([1], [-1], [5000]));
        manager.Active.Should().BeNull();
    }

    [Fact]
    public void Slow_tick_only_triggers_on_root_sections() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture(c => {
            c.Sampling.FocusedCaptureEnabled = true;
            c.Session.SlowTickThresholdUs = 1000;
        });

        // A slow child (parent != -1) must not trigger a capture.
        aggregator.OnSectionBatch(Batch([2], [1], [5000]));
        manager.Active.Should().BeNull();
    }

    [Fact]
    public void Size_cap_finalizes_capture_when_estimate_exceeds_limit() {
        (SessionAggregator aggregator, CaptureManager manager, _) = NewFixture(c => {
            // Smallest non-zero cap: 1 MB holds ~21k edges at 48 bytes each.
            c.Storage.MaxCaptureSizeMb = 1;
        });

        CaptureSession capture = manager.Start(CaptureTrigger.Manual);

        int n = 30_000;
        int[] ids = new int[n];
        int[] parents = new int[n];
        long[] elapsed = new long[n];
        for (int i = 0; i < n; i++) {
            ids[i] = i;
            parents[i] = CallTreeBuilder.NoParent;
            elapsed[i] = 1;
        }
        aggregator.OnSectionBatch(Batch(ids, parents, elapsed));

        capture.IsRunning.Should().BeFalse();
        capture.FinalizeReason.Should().Be(CaptureFinalizeReason.SizeCap);
        capture.DroppedSamples.Should().BeGreaterThan(0);
        manager.Active.Should().BeNull();
    }

    [Fact]
    public void Time_cap_auto_finalizes_capture_past_max_duration() {
        ConfigStore config = new(configFilePath: null);
        RimObsConfig cfg = config.Current;
        cfg.Capture.MaxDurationMinutes = 5;
        config.Replace(cfg);

        SessionAggregator aggregator = new();
        aggregator.OnSessionMeta(new SessionMeta { SessionId = "cap-test", StopwatchFrequency = 1_000_000 });

        DateTime now = new(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);
        CaptureManager manager = new(aggregator, config, () => now);

        CaptureSession capture = manager.Start(CaptureTrigger.Manual);

        // Within the cap: no-op.
        now = now.AddMinutes(4);
        manager.EnforceTimeCap();
        capture.IsRunning.Should().BeTrue();

        // Past the 5-minute safety cap: auto-finalizes with a warning reason.
        now = now.AddMinutes(2);
        manager.EnforceTimeCap();
        capture.IsRunning.Should().BeFalse();
        capture.FinalizeReason.Should().Be(CaptureFinalizeReason.TimeCap);
        manager.Active.Should().BeNull();
    }

    [Fact]
    public void Snapshot_returns_finalized_captures_newest_first() {
        (_, CaptureManager manager, _) = NewFixture();

        CaptureSession first = manager.Start(CaptureTrigger.Manual);
        manager.Stop();
        CaptureSession second = manager.Start(CaptureTrigger.Manual);
        manager.Stop();

        manager.Snapshot().Select(c => c.CaptureId).Should().ContainInOrder(second.CaptureId, first.CaptureId);
    }
}
