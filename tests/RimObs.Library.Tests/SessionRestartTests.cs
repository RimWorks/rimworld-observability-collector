using System;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Session;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

// A session is one launch of the game, so splitting one mid-play is the only way to get runs
// that can be named and compared. These pin the two things that make the split usable.
public sealed class SessionRestartTests {
    // SessionAnchor is static and shared by the whole assembly, so every case sets its own
    // baseline with Restart rather than assuming Initialize has never run.
    [Fact]
    public void Restart_moves_the_session_onto_a_new_id() {
        SessionAnchor.Restart("original-session");

        SessionAnchor.Restart("second-session");

        SessionAnchor.SessionId.Should().Be("second-session");
    }

    // Initialize is one-shot on purpose, so a restart must not go through it or the id sticks.
    [Fact]
    public void Restart_overwrites_an_anchor_Initialize_would_refuse_to_touch() {
        SessionAnchor.Restart("first");
        SessionAnchor.Initialize("ignored");
        SessionAnchor.SessionId.Should().Be("first");

        SessionAnchor.Restart("third");

        SessionAnchor.SessionId.Should().Be("third");
    }

    // every sample is timed against the anchor, so keeping the old one would date the new
    // session's frames to the previous run.
    [Fact]
    public void Restart_re_anchors_the_clock() {
        SessionAnchor.Restart("clock-a");
        long before = SessionAnchor.AnchorTimestamp;

        SessionAnchor.Restart("clock-b");

        SessionAnchor.AnchorTimestamp.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Restart_rejects_an_empty_id() {
        Action act = () => SessionAnchor.Restart("");

        act.Should().Throw<ArgumentException>();
    }

    // a new session starts the collector's name table empty, so without a requeue every
    // section in the new session arrives as a bare id with no name.
    [Fact]
    public void Requeueing_offers_every_registered_section_again() {
        SectionRegistry.Clear();
        SectionRegistry.Register("requeue-one");
        SectionRegistry.Register("requeue-two");

        int[] ids = new int[16];
        string[] names = new string[16];
        string?[] subsystems = new string?[16];
        SectionRegistry.DrainPendingRegistrations(ids, names, subsystems).Should().Be(2);
        SectionRegistry.DrainPendingRegistrations(ids, names, subsystems).Should().Be(0);

        SectionRegistry.RequeueAllRegistrations();

        SectionRegistry.DrainPendingRegistrations(ids, names, subsystems).Should().Be(2);
        names[0].Should().Be("requeue-one");
        names[1].Should().Be("requeue-two");
    }

    [Fact]
    public void Requeueing_an_empty_registry_queues_nothing() {
        SectionRegistry.Clear();

        SectionRegistry.RequeueAllRegistrations();

        int[] ids = new int[4];
        string[] names = new string[4];
        string?[] subsystems = new string?[4];
        SectionRegistry.DrainPendingRegistrations(ids, names, subsystems).Should().Be(0);
    }
}
