using System;
using System.Reflection;
using RimWorks.RimObs.Api;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class OwnerRegistryTests : IDisposable {
    private readonly Assembly _self = typeof(OwnerRegistryTests).Assembly;

    public OwnerRegistryTests() {
        OwnerRegistry.Clear();
    }

    public void Dispose() {
        OwnerRegistry.Clear();
    }

    [Fact]
    public void RegisterMod_then_TryGetPackageId_round_trips() {
        OwnerRegistry.RegisterMod(_self, "test.pkg");

        OwnerRegistry.TryGetPackageId(_self, out string? id).Should().BeTrue();
        id.Should().Be("test.pkg");
    }

    [Fact]
    public void TryGetPackageId_returns_false_when_missing_and_no_resolver() {
        OwnerRegistry.TryGetPackageId(_self, out string? id).Should().BeFalse();
        id.Should().BeNull();
    }

    [Fact]
    public void Late_resolver_is_consulted_on_cache_miss() {
        OwnerRegistry.SetLateResolver(asm => asm == _self ? "late.pkg" : null);

        OwnerRegistry.TryGetPackageId(_self, out string? id).Should().BeTrue();
        id.Should().Be("late.pkg");
    }

    [Fact]
    public void Late_resolver_result_is_cached_for_subsequent_lookups() {
        int calls = 0;
        OwnerRegistry.SetLateResolver(_ => {
            calls++;
            return "cached.pkg";
        });

        OwnerRegistry.TryGetPackageId(_self, out _).Should().BeTrue();
        OwnerRegistry.TryGetPackageId(_self, out _).Should().BeTrue();
        OwnerRegistry.TryGetPackageId(_self, out _).Should().BeTrue();

        calls.Should().Be(1);
    }

    [Fact]
    public void Late_resolver_returning_null_leaves_registry_unchanged() {
        OwnerRegistry.SetLateResolver(_ => null);

        OwnerRegistry.TryGetPackageId(_self, out string? id).Should().BeFalse();
        id.Should().BeNull();
        OwnerRegistry.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_drops_late_resolver() {
        OwnerRegistry.SetLateResolver(_ => "should.not.see");
        OwnerRegistry.Clear();

        OwnerRegistry.TryGetPackageId(_self, out string? id).Should().BeFalse();
        id.Should().BeNull();
    }

    [Fact]
    public void ResolveOrThrow_uses_late_resolver_before_throwing() {
        OwnerRegistry.SetLateResolver(_ => "resolved.pkg");

        OwnerRegistry.ResolveOrThrow(_self).Should().Be("resolved.pkg");
    }

    [Fact]
    public void ResolveOrThrow_throws_when_neither_registry_nor_resolver_matches() {
        Action act = () => OwnerRegistry.ResolveOrThrow(_self);

        act.Should().Throw<InvalidOperationException>();
    }
}
