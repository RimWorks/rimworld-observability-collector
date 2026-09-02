using System;
using System.Collections.Generic;
using System.Threading;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class AllocationSamplerTests {
    [Fact]
    public void Initial_poll_returns_false_when_window_not_elapsed() {
        AllocationSampler sampler = new();

        bool got = sampler.TryPollWindow(windowDurationMs: 60_000, out _);

        got.Should().BeFalse();
        sampler.TotalSamplesEmitted.Should().Be(0);
    }

    [Fact]
    public void Window_emits_after_duration_elapsed() {
        AllocationSampler sampler = new();

        Thread.Sleep(20);

        bool got = sampler.TryPollWindow(windowDurationMs: 10, out AllocationSample sample);

        got.Should().BeTrue();
        sampler.TotalSamplesEmitted.Should().Be(1);
        sample.WindowDurationMs.Should().BeGreaterThanOrEqualTo(10);
        sample.BytesAllocated.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Bytes_accumulator_resets_each_window() {
        AllocationSampler sampler = new();

        // long.MaxValue banks each delta without emitting, so a slow runner cannot emit early and
        // discard the growth. Only a positive delta banks, so one GC cannot flatten eight chunks.
        List<byte[]> held = new(8);
        for (int i = 0; i < 8; i++) {
            byte[] chunk = new byte[500_000];
            chunk[0] = 1;
            held.Add(chunk);
            sampler.TryPollWindow(long.MaxValue, out _);
        }
        GC.KeepAlive(held);

        Thread.Sleep(20);
        sampler.TryPollWindow(10, out AllocationSample first).Should().BeTrue();

        Thread.Sleep(20);
        sampler.TryPollWindow(10, out AllocationSample second).Should().BeTrue();

        first.BytesAllocated.Should().BeGreaterThan(0);
        second.WindowStartTimestamp.Should().BeGreaterThan(first.WindowStartTimestamp);
    }

    [Fact]
    public void AllocationSample_carries_fields() {
        AllocationSample s = new(windowStartTimestamp: 100, windowDurationMs: 60_000, bytesAllocated: 12345, samplesCount: 60);

        s.WindowStartTimestamp.Should().Be(100);
        s.WindowDurationMs.Should().Be(60_000);
        s.BytesAllocated.Should().Be(12345);
        s.SamplesCount.Should().Be(60);
    }
}

public sealed class AllocationSamplerHostTests : IDisposable {
    public AllocationSamplerHostTests() {
        AllocationSamplerHost.Stop();
        AllocationSamplerHost.ClearRecentSamples();
        AllocationSamplerHost.WindowDurationMs = 60_000;
        AllocationSamplerHost.SetSink(null);
    }

    public void Dispose() {
        AllocationSamplerHost.Stop();
        AllocationSamplerHost.ClearRecentSamples();
        AllocationSamplerHost.WindowDurationMs = 60_000;
        AllocationSamplerHost.SetSink(null);
    }

    [Fact]
    public void Instance_returns_singleton() {
        AllocationSampler a = AllocationSamplerHost.Instance;
        AllocationSampler b = AllocationSamplerHost.Instance;

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Default_state_is_not_running() {
        AllocationSamplerHost.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_and_Stop_round_trip() {
        AllocationSamplerHost.Start();
        AllocationSamplerHost.IsRunning.Should().BeTrue();

        AllocationSamplerHost.Stop();
        AllocationSamplerHost.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void PollOnce_does_not_emit_until_window_elapsed() {
        AllocationSamplerHost.WindowDurationMs = 60_000;

        bool got = AllocationSamplerHost.PollOnce();

        got.Should().BeFalse();
        AllocationSamplerHost.RecentSamples.Should().BeEmpty();
    }

    [Fact]
    public void PollOnce_appends_sample_when_window_elapses() {
        AllocationSamplerHost.WindowDurationMs = 10;
        Thread.Sleep(15);

        bool got = AllocationSamplerHost.PollOnce();

        got.Should().BeTrue();
        AllocationSamplerHost.RecentSamples.Should().NotBeEmpty();
    }

    [Fact]
    public void Start_is_idempotent() {
        AllocationSamplerHost.Start();
        AllocationSamplerHost.Start();

        AllocationSamplerHost.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void PollOnce_forwards_samples_to_attached_sink() {
        RecordingAllocSink sink = new();
        AllocationSamplerHost.SetSink(sink);
        AllocationSamplerHost.WindowDurationMs = 10;
        Thread.Sleep(15);

        bool got = AllocationSamplerHost.PollOnce();

        got.Should().BeTrue();
        sink.Received.Should().NotBeEmpty();
    }

    [Fact]
    public void SetSink_null_detaches_sink() {
        RecordingAllocSink sink = new();
        AllocationSamplerHost.SetSink(sink);
        AllocationSamplerHost.SetSink(null);
        AllocationSamplerHost.WindowDurationMs = 10;
        Thread.Sleep(15);

        AllocationSamplerHost.PollOnce();

        sink.Received.Should().BeEmpty();
    }

    private sealed class RecordingAllocSink : IAllocationSink {
        public List<AllocationSample> Received { get; } = new();

        public void RecordAllocation(in AllocationSample sample) {
            Received.Add(sample);
        }
    }
}
