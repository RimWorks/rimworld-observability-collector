using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using RimWorks.RimObs.Patching;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class PatchBackendsTests : IDisposable {
    public PatchBackendsTests() => PatchBackends.ResetForTests();

    public void Dispose() => PatchBackends.ResetForTests();

    private sealed class FakeBackend : IPatchBackend {
        public FakeBackend(string name) => Name = name;
        public string Name { get; }
        public void Patch(MethodBase target) { }
        public void PatchPrefix(MethodBase target, MethodInfo prefix) { }
        public void PatchPostfix(MethodBase target, MethodInfo postfix) { }
        public void Unpatch(MethodBase target) { }
        public IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target) => Array.Empty<ForeignPatch>();
        public void UnpatchAllForTests() { }
    }

    [Fact]
    public void HighestPriorityWins() {
        PatchBackends.Register(new FakeBackend("Harmony"), PatchBackends.HarmonyPriority);
        PatchBackends.Register(new FakeBackend("Concord"), PatchBackends.ConcordPriority);

        PatchBackends.SelectBest();

        PatchBackends.Active!.Name.Should().Be("Concord");
    }

    [Fact]
    public void ASingleBackendWinsUnopposed() {
        PatchBackends.Register(new FakeBackend("Harmony"), PatchBackends.HarmonyPriority);

        PatchBackends.SelectBest();

        PatchBackends.Active!.Name.Should().Be("Harmony");
    }

    // regression: with no library loaded the mod must degrade to "no instrumentation",
    // never throw during bootstrap.
    [Fact]
    public void NoBackendLeavesActiveNull() {
        PatchBackends.SelectBest();

        PatchBackends.Active.Should().BeNull();
    }

    [Fact]
    public void SelectingTwiceKeepsTheFirstWinner() {
        PatchBackends.Register(new FakeBackend("Harmony"), PatchBackends.HarmonyPriority);
        PatchBackends.SelectBest();
        PatchBackends.Register(new FakeBackend("Concord"), PatchBackends.ConcordPriority);
        PatchBackends.SelectBest();

        PatchBackends.Active!.Name.Should().Be("Harmony");
    }
}
