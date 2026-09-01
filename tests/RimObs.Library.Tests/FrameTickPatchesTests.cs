using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class FrameTickPatchesTests : IDisposable {
    private readonly RecordingBackend _backend = new("Recording");

    public FrameTickPatchesTests() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        PatchBackends.ResetForTests();
        PatchBackends.Register(_backend, PatchBackends.HarmonyPriority);
        PatchBackends.SelectBest(scan: false);
    }

    public void Dispose() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        PatchBackends.ResetForTests();
    }

    // regression: the two targets used to come from AccessTools. they now come from the core
    // pack's resolved entries, so a rename there would silently kill TPS/FPS.
    [Fact]
    public void PatchesTheTickAndFrameSectionsFromTheCorePack() {
        MethodInfo tick = typeof(Targets).GetMethod(nameof(Targets.Tick))!;
        MethodInfo frame = typeof(Targets).GetMethod(nameof(Targets.Frame))!;
        SectionCatalog.RegisterDirect(FrameTickPatches.TickSection, tick);
        SectionCatalog.RegisterDirect(FrameTickPatches.FrameSection, frame);
        int before = FrameTickPatches.InstalledCount;

        FrameTickPatches.InstallAll();

        FrameTickPatches.InstalledCount.Should().Be(before + 3);
        _backend.Prefixes.Should().ContainSingle(p =>
            ReferenceEquals(p.Target, tick) && p.Injection.Name == "DrainControlOpsPrefix");
        _backend.Postfixes.Should().HaveCount(2);
        _backend.Postfixes.Should().Contain(p =>
            ReferenceEquals(p.Target, tick) && p.Injection.Name == "TickPostfix");
        _backend.Postfixes.Should().Contain(p =>
            ReferenceEquals(p.Target, frame) && p.Injection.Name == "FramePostfix");
    }

    // regression: dropping either line from s_CorePackSections leaves the section unresolved
    // at boot, and TPS/FPS goes dark with nothing to say about it.
    [Fact]
    public void TheCorePackRegistersBothSections() {
        SectionCatalog.RegisterCorePack();

        SectionCatalog.Entries.Should().Contain(e => e.Name == FrameTickPatches.TickSection);
        SectionCatalog.Entries.Should().Contain(e => e.Name == FrameTickPatches.FrameSection);
    }

    [Fact]
    public void InstallsNothingWhenTheSectionsDidNotResolve() {
        int before = FrameTickPatches.InstalledCount;

        FrameTickPatches.InstallAll();

        FrameTickPatches.InstalledCount.Should().Be(before);
        _backend.Postfixes.Should().BeEmpty();
    }

    [Fact]
    public void InstallsNothingWithoutABackend() {
        SectionCatalog.RegisterDirect(
            FrameTickPatches.TickSection, typeof(Targets).GetMethod(nameof(Targets.Tick))!);
        PatchBackends.ResetForTests();
        int before = FrameTickPatches.InstalledCount;

        FrameTickPatches.InstallAll();

        FrameTickPatches.InstalledCount.Should().Be(before);
        _backend.Postfixes.Should().BeEmpty();
    }

    // the ctor argument is load-bearing: it keeps ScanForBackends from constructing this fake.
    private sealed class RecordingBackend : IPatchBackend {
        public RecordingBackend(string name) => Name = name;

        public List<(MethodBase Target, MethodInfo Injection)> Prefixes { get; } = new();
        public List<(MethodBase Target, MethodInfo Injection)> Postfixes { get; } = new();

        public string Name { get; }

        public void Patch(MethodBase target) { }

        public void PatchPrefix(MethodBase target, MethodInfo prefix) => Prefixes.Add((target, prefix));

        public void PatchPostfix(MethodBase target, MethodInfo postfix) => Postfixes.Add((target, postfix));

        public void Unpatch(MethodBase target) { }

        public IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target) => Array.Empty<ForeignPatch>();

        public void UnpatchAllForTests() { }
    }

    public static class Targets {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Tick() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Frame() { }
    }
}
