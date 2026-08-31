using System;
using System.Collections.Generic;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class GcObserverHostTests : IDisposable {
    public GcObserverHostTests() {
        GcObserverHost.Stop();
        GcObserverHost.ClearRecentSamples();
        GcObserverHost.SetSink(null);
    }

    public void Dispose() {
        GcObserverHost.Stop();
        GcObserverHost.ClearRecentSamples();
        GcObserverHost.SetSink(null);
    }

    [Fact]
    public void Instance_returns_singleton() {
        GcObserver a = GcObserverHost.Instance;
        GcObserver b = GcObserverHost.Instance;

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void PollOnce_appends_to_recent_events_when_collection_occurs() {
        GcObserverHost.PollOnce(0);

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);

        bool detected = GcObserverHost.PollOnce(42);

        detected.Should().BeTrue();
        var events = GcObserverHost.RecentSamples;
        events.Should().NotBeEmpty();
        events[events.Count - 1].Tick.Should().Be(42);
    }

    [Fact]
    public void Start_and_Stop_round_trip() {
        GcObserverHost.IsRunning.Should().BeFalse();

        GcObserverHost.Start();
        GcObserverHost.IsRunning.Should().BeTrue();

        GcObserverHost.Stop();
        GcObserverHost.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_is_idempotent() {
        GcObserverHost.Start();
        GcObserverHost.Start();

        GcObserverHost.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void ClearRecentSamples_empties_buffer() {
        GcObserverHost.PollOnce(0);
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        GcObserverHost.PollOnce(1);

        GcObserverHost.ClearRecentSamples();

        GcObserverHost.RecentSamples.Should().BeEmpty();
    }

    [Fact]
    public void PollOnce_forwards_samples_to_attached_sink() {
        RecordingGcSink sink = new();
        GcObserverHost.SetSink(sink);

        GcObserverHost.PollOnce(0);
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        GcObserverHost.PollOnce(99);

        sink.Received.Should().NotBeEmpty();
        sink.Received[sink.Received.Count - 1].Tick.Should().Be(99);
    }

    [Fact]
    public void SetSink_null_detaches_sink() {
        RecordingGcSink sink = new();
        GcObserverHost.SetSink(sink);
        GcObserverHost.SetSink(null);

        GcObserverHost.PollOnce(0);
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        GcObserverHost.PollOnce(7);

        sink.Received.Should().BeEmpty();
    }

    private sealed class RecordingGcSink : IGcEventSink {
        public List<GcEventSample> Received { get; } = new();

        public void RecordGcEvent(in GcEventSample sample) {
            Received.Add(sample);
        }
    }
}
