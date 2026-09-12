using System;
using FluentAssertions;
using RimWorks.RimObs.Observers;
using Xunit;

namespace RimWorks.RimObs.Library.Tests;

// regression: a bootstrap-time enable put the boehm callback live during worldgen's wrapper
// JIT and segfaulted mono, so the enable parks and the frame prefix arms it once ticking.
public sealed class AllocationHookDeferralTests {
    [Fact]
    public void Deferral_is_pending_after_the_bootstrap_parks_it() {
        AllocationHook.DeferEnable();

        AllocationHook.DeferredPending.Should().BeTrue();
    }

    [Fact]
    public void Arming_consumes_the_deferral_and_never_retries() {
        AllocationHook.DeferEnable();

        // the native result depends on the host (system mono can genuinely resolve);
        // the contract under test is the one-shot deferral, not the hook itself.
        AllocationHook.TryEnableDeferred();

        AllocationHook.DeferredPending.Should().BeFalse();
        AllocationHook.TryEnableDeferred().Should().BeFalse();
    }

    [Fact]
    public void Suspend_scopes_refcount_and_nest_across_owners() {
        using (AllocationHook.SuspendScope()) {
            AllocationHook.SuspendCountForTests.Should().Be(1);
            using (AllocationHook.SuspendScope())
                AllocationHook.SuspendCountForTests.Should().Be(2);
            AllocationHook.SuspendCountForTests.Should().Be(1);
        }
        AllocationHook.SuspendCountForTests.Should().Be(0);
    }

    [Fact]
    public void Disposing_a_suspend_token_twice_releases_once() {
        IDisposable token = AllocationHook.SuspendScope();
        token.Dispose();
        token.Dispose();

        AllocationHook.SuspendCountForTests.Should().Be(0);
    }

    [Fact]
    public void Arming_without_a_parked_deferral_does_nothing() {
        AllocationHook.TryEnableDeferred().Should().BeFalse();
        AllocationHook.DeferredPending.Should().BeFalse();
    }

    // regression: a tick-count gate armed the hook during map generation, because rimworld
    // ticks while generating; only the injected verse gate may decide "playing".
    [Fact]
    public void Frame_prefix_leaves_the_deferral_parked_while_the_gate_says_not_playing() {
        AllocationHook.DeferEnable();
        Patching.FrameTickPatches.AllocArmGate = static () => false;
        try {
            Patching.FrameTickPatches.FrameBeginPrefix();

            AllocationHook.DeferredPending.Should().BeTrue();
        }
        finally {
            Patching.FrameTickPatches.AllocArmGate = null;
            AllocationHook.TryEnableDeferred();
        }
    }

    [Fact]
    public void Frame_prefix_arms_once_the_gate_says_playing() {
        AllocationHook.DeferEnable();
        Patching.FrameTickPatches.AllocArmGate = static () => true;
        try {
            Patching.FrameTickPatches.FrameBeginPrefix();

            AllocationHook.DeferredPending.Should().BeFalse();
        }
        finally {
            Patching.FrameTickPatches.AllocArmGate = null;
        }
    }

    [Fact]
    public void Frame_prefix_never_arms_without_a_gate() {
        AllocationHook.DeferEnable();
        try {
            Patching.FrameTickPatches.FrameBeginPrefix();

            AllocationHook.DeferredPending.Should().BeTrue();
        }
        finally {
            AllocationHook.TryEnableDeferred();
        }
    }
}
