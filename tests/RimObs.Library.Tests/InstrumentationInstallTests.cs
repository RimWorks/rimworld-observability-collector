using System;
using System.Collections.Generic;
using FluentAssertions;
using RimWorks.RimObs.Bootstrap;
using Xunit;

namespace RimObs.Library.Tests;

public class InstrumentationInstallTests {
    public InstrumentationInstallTests() => InstrumentationInstall.ResetForTests();

    // regression: patching runs each target's static ctor, RimWorld's UI types build materials
    // there, and Unity refuses that off the main thread. the install must never run inline.
    [Fact]
    public void DoesNotRunTheInstallInline() {
        bool ran = false;
        Action? captured = null;

        InstrumentationInstall.Schedule(work => captured = work, () => ran = true);

        ran.Should().BeFalse();
        captured.Should().NotBeNull();

        captured!();
        ran.Should().BeTrue();
    }

    [Fact]
    public void SchedulesOnlyOnce() {
        List<Action> scheduled = new();

        InstrumentationInstall.Schedule(scheduled.Add, () => { });
        InstrumentationInstall.Schedule(scheduled.Add, () => { });

        scheduled.Should().HaveCount(1);
    }

    [Fact]
    public void SurfacesASchedulerThatRefusesTheWork() {
        Action act = () => InstrumentationInstall.Schedule(_ => throw new InvalidOperationException("no"), () => { });

        act.Should().Throw<InvalidOperationException>();
    }
}
