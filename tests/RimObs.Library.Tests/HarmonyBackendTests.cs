using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using HarmonyLib;
using RimWorks.RimObs.Patches.Harmony;
using RimWorks.RimObs.Patching;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class HarmonyBackendTests : IDisposable {
    private const string ForeignId = "some.other.mod";

    private readonly HarmonyBackend _backend = new();
    private readonly Harmony _foreign = new(ForeignId);

    public HarmonyBackendTests() => PatchBackends.ResetForTests();

    public void Dispose() {
        _foreign.UnpatchAll(ForeignId);
        _backend.UnpatchAllForTests();
        PatchBackends.ResetForTests();
    }

    [Fact]
    public void NamesItself() {
        _backend.Name.Should().Be("Harmony");
    }

    [Fact]
    public void RegistersItselfAtTheHarmonyPriority() {
        PatchBackends.Register(_backend, PatchBackends.HarmonyPriority);
        PatchBackends.SelectBest(scan: false);

        PatchBackends.Active!.Name.Should().Be("Harmony");
    }

    [Fact]
    public void ReportsAnotherModsPatchButNotItsOwn() {
        MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.Instrumented))!;
        _backend.Patch(target);
        _foreign.Patch(target, postfix: new HarmonyMethod(typeof(Targets).GetMethod(nameof(Targets.Postfix))));

        IReadOnlyList<ForeignPatch> conflicts = _backend.ConflictsFor(target);

        conflicts.Should().ContainSingle();
        conflicts[0].Owner.Should().Be(ForeignId);
        conflicts[0].Kind.Should().Be(PatchKind.Postfix);
        conflicts[0].PatchMethod.Should().EndWith(nameof(Targets.Postfix));
    }

    [Fact]
    public void UnpatchRemovesOnlyTheTranspiler() {
        MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.AlsoInstrumented))!;
        _backend.Patch(target);
        _foreign.Patch(target, postfix: new HarmonyMethod(typeof(Targets).GetMethod(nameof(Targets.Postfix))));
        Harmony.GetPatchInfo(target)!.Transpilers.Should().ContainSingle();

        _backend.Unpatch(target);

        HarmonyLib.Patches info = Harmony.GetPatchInfo(target)!;
        info.Transpilers.Should().BeEmpty();
        info.Postfixes.Should().ContainSingle();
    }

    public static class Targets {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Instrumented() {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AlsoInstrumented() {
        }

        public static void Postfix() {
        }
    }
}
