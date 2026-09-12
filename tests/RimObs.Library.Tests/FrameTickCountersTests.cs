using System.Threading.Tasks;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class FrameTickCountersTests {
    public FrameTickCountersTests() {
        FrameTickCounters.Reset();
    }

    // the count change is noticed AFTER BeginFrame bumps the ordinal, so the frame that
    // actually stalled under the collection is the previous one.
    [Fact]
    public void Notes_the_frame_a_collection_landed_in() {
        FrameTickCounters.BeginFrame();
        FrameTickCounters.NoteCollections(0);
        FrameTickCounters.BeginFrame();
        FrameTickCounters.BeginFrame();
        FrameTickCounters.NoteCollections(1);

        int stalled = FrameTickCounters.FrameOrdinal - 1;

        for (int i = 0; i < 60; i++) {
            FrameTickCounters.BeginFrame();
            FrameTickCounters.NoteCollections(1);
        }

        FrameTickCounters.LastGcFrameOrdinal.Should().Be(stalled);
        FrameTickCounters.FrameOrdinal.Should().Be(stalled + 61);
    }

    [Fact]
    public void An_unchanged_collection_count_never_moves_the_gc_frame() {
        FrameTickCounters.BeginFrame();
        FrameTickCounters.NoteCollections(4);
        int first = FrameTickCounters.LastGcFrameOrdinal;

        FrameTickCounters.BeginFrame();
        FrameTickCounters.NoteCollections(4);

        FrameTickCounters.LastGcFrameOrdinal.Should().Be(first);
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

    [Fact]
    public void BeginFrame_advances_the_ordinal_from_zero() {
        FrameTickCounters.FrameOrdinal.Should().Be(0);

        FrameTickCounters.BeginFrame();
        FrameTickCounters.BeginFrame();

        FrameTickCounters.FrameOrdinal.Should().Be(2);
    }

    [Fact]
    public void Reset_zeroes_the_frame_ordinal() {
        FrameTickCounters.BeginFrame();

        FrameTickCounters.Reset();

        FrameTickCounters.FrameOrdinal.Should().Be(0);
    }

    [Fact]
    public void BeginFrame_does_not_move_the_fps_frame_count() {
        FrameTickCounters.BeginFrame();

        FrameTickCounters.Frames.Should().Be(0);
        FrameTickCounters.FrameOrdinal.Should().Be(1);
    }
}
