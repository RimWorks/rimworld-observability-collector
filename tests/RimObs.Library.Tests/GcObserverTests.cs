using System;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class GcObserverTests {
    public GcObserverTests() {
        FrameTickCounters.Reset();
    }

    [Fact]
    public void Initial_poll_with_no_collection_returns_false() {
        GcObserver observer = new();

        bool detected = observer.TryPoll(currentTick: 0, out _);

        detected.Should().BeFalse();
        observer.EventsObserved.Should().Be(0);
    }

    [Fact]
    public void Forced_gen0_collection_is_detected() {
        GcObserver observer = new();
        observer.TryPoll(0, out _);

        GC.Collect(generation: 0, mode: GCCollectionMode.Forced, blocking: true);

        bool detected = observer.TryPoll(currentTick: 123, out GcEventSample sample);

        detected.Should().BeTrue();
        observer.EventsObserved.Should().Be(1);
        sample.Tick.Should().Be(123);
        sample.Generation.Should().BeLessThanOrEqualTo((byte)observer.MaxGeneration);
        sample.PauseType.Should().Be(GcPauseType.Foreground);
    }

    [Fact]
    public void Highest_generation_change_is_reported_when_multiple_change() {
        GcObserver observer = new();
        observer.TryPoll(0, out _);

        GC.Collect(generation: observer.MaxGeneration, mode: GCCollectionMode.Forced, blocking: true);

        bool detected = observer.TryPoll(currentTick: 7, out GcEventSample sample);

        detected.Should().BeTrue();
        sample.Generation.Should().Be((byte)observer.MaxGeneration);
    }

    [Fact]
    public void Subsequent_poll_after_event_returns_false_until_next_collection() {
        GcObserver observer = new();
        observer.TryPoll(0, out _);

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        observer.TryPoll(1, out _);

        bool detected = observer.TryPoll(currentTick: 2, out _);

        detected.Should().BeFalse();
    }

    [Fact]
    public void Allocation_rate_updates_on_heap_growth() {
        GcObserver observer = new();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        observer.TryPoll(0, out _);

        byte[]? big = new byte[2_000_000];
        big[0] = 1;

        observer.TryPoll(1, out _);

        observer.AllocationRateBytesPerMinute.Should().BeGreaterThan(0);
        GC.KeepAlive(big);
    }

    [Fact]
    public void GcEventSample_carries_all_fields() {
        GcEventSample sample = new(generation: 1, pauseType: GcPauseType.Background, heapBefore: 100, heapAfter: 80, durationMicros: 250, tick: 99, allocationRateBytesPerMinute: 1024, frameOrdinal: 42);

        sample.Generation.Should().Be(1);
        sample.PauseType.Should().Be(GcPauseType.Background);
        sample.HeapBefore.Should().Be(100);
        sample.HeapAfter.Should().Be(80);
        sample.DurationMicros.Should().Be(250);
        sample.Tick.Should().Be(99);
        sample.AllocationRateBytesPerMinute.Should().Be(1024);
        sample.FrameOrdinal.Should().Be(42);
    }

    [Fact]
    // the poller wakes long after the collection, so the sample must carry the frame the
    // per-frame counter saw it in, not the frame the poller happens to be in now.
    public void Detected_collection_reports_the_frame_the_collection_landed_in() {
        GcObserver observer = new();
        observer.TryPoll(0, out _);
        FrameTickCounters.BeginFrame();
        FrameTickCounters.BeginFrame();
        FrameTickCounters.BeginFrame();

        GC.Collect(generation: 0, mode: GCCollectionMode.Forced, blocking: true);
        FrameTickCounters.NoteCollections(GC.CollectionCount(0));
        int landedOn = FrameTickCounters.FrameOrdinal;

        for (int i = 0; i < 40; i++)
            FrameTickCounters.BeginFrame();

        bool detected = observer.TryPoll(currentTick: 1, out GcEventSample sample);

        detected.Should().BeTrue();
        sample.FrameOrdinal.Should().Be(landedOn);
        FrameTickCounters.FrameOrdinal.Should().Be(landedOn + 40);
    }
}
