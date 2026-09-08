using System;
using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class NativeCounterProbeTests {
    [Fact]
    public void TryRead_uses_first_candidate_that_does_not_throw() {
        NativeCounterProbe probe = new(
            () => throw new DllNotFoundException(),
            () => 42L);

        bool got = probe.TryRead(out long value);

        got.Should().BeTrue();
        value.Should().Be(42L);
    }

    [Fact]
    public void TryRead_treats_missing_entry_point_the_same_as_missing_library() {
        NativeCounterProbe probe = new(
            () => throw new EntryPointNotFoundException(),
            () => 7L);

        bool got = probe.TryRead(out long value);

        got.Should().BeTrue();
        value.Should().Be(7L);
    }

    [Fact]
    public void TryRead_returns_false_when_no_candidate_resolves() {
        NativeCounterProbe probe = new(
            () => throw new DllNotFoundException(),
            () => throw new EntryPointNotFoundException());

        bool got = probe.TryRead(out long value);

        got.Should().BeFalse();
        value.Should().Be(0L);
    }

    [Fact]
    public void TryRead_resolves_once_and_never_probes_the_losing_candidates_again() {
        int firstCandidateCalls = 0;
        int secondCandidateCalls = 0;
        NativeCounterProbe probe = new(
            () => { firstCandidateCalls++; throw new DllNotFoundException(); },
            () => { secondCandidateCalls++; return 1L; });

        probe.TryRead(out _);
        probe.TryRead(out _);
        probe.TryRead(out _);

        firstCandidateCalls.Should().Be(1);
        secondCandidateCalls.Should().Be(3);
    }

    [Fact]
    public void TryRead_reflects_the_winning_candidates_current_value_on_each_call() {
        long current = 10L;
        NativeCounterProbe probe = new(() => current);

        probe.TryRead(out long first);
        current = 25L;
        probe.TryRead(out long second);

        first.Should().Be(10L);
        second.Should().Be(25L);
    }
}
