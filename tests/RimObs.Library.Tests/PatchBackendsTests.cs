using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using RimWorks.RimObs.Patches.Harmony;
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
        public bool SupportsConflictReporting => true;
        public IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target) => Array.Empty<ForeignPatch>();
        public void UnpatchAllForTests() { }
    }

    [Fact]
    public void HighestPriorityWins() {
        PatchBackends.Register(new FakeBackend("Harmony"), PatchBackends.HarmonyPriority);
        PatchBackends.Register(new FakeBackend("Concord"), PatchBackends.ConcordPriority);

        PatchBackends.SelectBest(scan: false);

        PatchBackends.Active!.Name.Should().Be("Concord");
    }

    [Fact]
    public void ASingleBackendWinsUnopposed() {
        PatchBackends.Register(new FakeBackend("Harmony"), PatchBackends.HarmonyPriority);

        PatchBackends.SelectBest(scan: false);

        PatchBackends.Active!.Name.Should().Be("Harmony");
    }

    // regression: with no library loaded the mod must degrade to "no instrumentation",
    // never throw during bootstrap. scan is off because this assembly compiles HarmonyBackend in.
    [Fact]
    public void NoBackendLeavesActiveNull() {
        PatchBackends.SelectBest(scan: false);

        PatchBackends.Active.Should().BeNull();
    }

    // regression: CallAll runs after our install is queued, so a backend that only registers from
    // its static constructor is invisible at SelectBest time. The scan is what finds it.
    [Fact]
    public void FindsABackendThatNeverRegistered() {
        PatchBackends.SelectBest();

        PatchBackends.Active.Should().NotBeNull(
            "the scan must find backends whose static constructors have not run yet");
    }

    // regression: the scan now runs even when a backend registered itself, so it must not add a
    // second instance. registering below HarmonyPriority means a scanned duplicate would win.
    [Fact]
    public void ScanSkipsATypeThatAlreadyRegistered() {
        HarmonyBackend backend = new();
        PatchBackends.Register(backend, PatchBackends.HarmonyPriority - 1);

        PatchBackends.SelectBest();

        PatchBackends.Active.Should().BeSameAs(backend);
    }

    [Fact]
    public void SelectingTwiceKeepsTheFirstWinner() {
        PatchBackends.Register(new FakeBackend("Harmony"), PatchBackends.HarmonyPriority);
        PatchBackends.SelectBest(scan: false);
        PatchBackends.Register(new FakeBackend("Concord"), PatchBackends.ConcordPriority);
        PatchBackends.SelectBest(scan: false);

        PatchBackends.Active!.Name.Should().Be("Harmony");
    }
}
