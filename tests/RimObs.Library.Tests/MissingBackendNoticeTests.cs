using System;
using FluentAssertions;
using RimWorks.RimObs.Patching;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class MissingBackendNoticeTests : IDisposable {
    public MissingBackendNoticeTests() => MissingBackendNotice.ResetForTests();

    public void Dispose() => MissingBackendNotice.ResetForTests();

    [Fact]
    public void YieldsTheTextOnTheFirstClaim() {
        MissingBackendNotice.ClaimOnce().Should().Be(MissingBackendNotice.Text);
    }

    [Fact]
    public void YieldsNothingOnEveryLaterClaim() {
        MissingBackendNotice.ClaimOnce().Should().NotBeNull();

        MissingBackendNotice.ClaimOnce().Should().BeNull();
        MissingBackendNotice.ClaimOnce().Should().BeNull();
    }

    [Fact]
    public void NamesBothLibrariesAndSaysWhichOneWins() {
        MissingBackendNotice.Text.Should().Contain("Harmony").And.Contain("Concord");
        MissingBackendNotice.Text.Should().Contain("uses Concord when both are active");
    }

    [Fact]
    public void CarriesBothWorkshopUrls() {
        MissingBackendNotice.Text.Should().Contain(MissingBackendNotice.HarmonyUrl);
        MissingBackendNotice.Text.Should().Contain(MissingBackendNotice.ConcordUrl);
    }

    // the notice replaces RimWorld's missing-dependency warning, so it has to say what broke.
    [Fact]
    public void SaysWhatStopsWorkingAndHowToFixIt() {
        MissingBackendNotice.Text.Should().Contain("cannot record anything from the game");
        MissingBackendNotice.Text.Should().Contain("restart RimWorld");
    }
}
