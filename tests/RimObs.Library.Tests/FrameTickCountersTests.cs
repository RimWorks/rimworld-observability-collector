using System.Threading.Tasks;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class FrameTickCountersTests {
    public FrameTickCountersTests() {
        FrameTickCounters.Reset();
    }

    [Fact]
    public void Reset_zeroes_both_counters() {
        FrameTickCounters.RecordTick();
        FrameTickCounters.RecordFrame();

        FrameTickCounters.Reset();

        FrameTickCounters.Ticks.Should().Be(0);
        FrameTickCounters.Frames.Should().Be(0);
    }

    [Fact]
    public void RecordTick_and_RecordFrame_track_independent_counters() {
        for (int i = 0; i < 7; i++)
            FrameTickCounters.RecordTick();
        for (int i = 0; i < 11; i++)
            FrameTickCounters.RecordFrame();

        FrameTickCounters.Ticks.Should().Be(7);
        FrameTickCounters.Frames.Should().Be(11);
    }

    [Fact]
    public void Concurrent_increments_do_not_lose_counts() {
        const int perWorker = 1000;
        const int workers = 8;
        Parallel.For(0, workers, _ => {
            for (int i = 0; i < perWorker; i++) {
                FrameTickCounters.RecordTick();
                FrameTickCounters.RecordFrame();
            }
        });

        FrameTickCounters.Ticks.Should().Be(perWorker * workers);
        FrameTickCounters.Frames.Should().Be(perWorker * workers);
    }
}
