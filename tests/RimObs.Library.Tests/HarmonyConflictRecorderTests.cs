using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using HarmonyLib;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class HarmonyConflictRecorderTests : IDisposable {
    private readonly Harmony _ourHarmony;
    private readonly Harmony _foreignHarmony;

    public HarmonyConflictRecorderTests() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        HarmonyConflictRecorder.Clear();
        _ourHarmony = new Harmony($"RimWorks.RimObs.tests.{Guid.NewGuid():N}");
        _foreignHarmony = new Harmony($"foreign.modder.{Guid.NewGuid():N}");
    }

    public void Dispose() {
        try { _ourHarmony.UnpatchAll(_ourHarmony.Id); } catch { }
        try { _foreignHarmony.UnpatchAll(_foreignHarmony.Id); } catch { }
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        HarmonyConflictRecorder.Clear();
    }

    [Fact]
    public void RecordConflicts_records_foreign_patches_on_tracked_section() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.Tracked))!;
        SectionCatalog.RegisterDirect("test.conflict.tracked", target);

        HarmonyMethod prefix = new(typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ForeignPrefix))!);
        _foreignHarmony.Patch(target, prefix: prefix);

        HarmonyConflictRecorder.RecordConflicts(_ourHarmony);

        HarmonyConflictRecorder.Count.Should().BeGreaterOrEqualTo(1);
        HarmonyConflictRecorder.Conflicts.Should().Contain(c =>
            c.SectionName == "test.conflict.tracked" &&
            c.OtherOwner == _foreignHarmony.Id &&
            c.PatchType == HarmonyPatchType.Prefix);
    }

    [Fact]
    public void RecordConflicts_skips_patches_owned_by_us() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.OwnedByUs))!;
        SectionCatalog.RegisterDirect("test.conflict.ours", target);

        HarmonyMethod prefix = new(typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.OurPrefix))!);
        _ourHarmony.Patch(target, prefix: prefix);

        HarmonyConflictRecorder.RecordConflicts(_ourHarmony);

        HarmonyConflictRecorder.Conflicts.Should().NotContain(c => c.SectionName == "test.conflict.ours");
    }

    [Fact]
    public void RecordConflicts_ignores_unresolved_entries() {
        SectionCatalog.Register("test.conflict.unresolved", "NoSuchType.Ever", "Op", null);

        Action act = () => HarmonyConflictRecorder.RecordConflicts(_ourHarmony);

        act.Should().NotThrow();
        HarmonyConflictRecorder.Conflicts.Should().NotContain(c => c.SectionName == "test.conflict.unresolved");
    }

    [Fact]
    public void Clear_empties_the_conflict_list() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ClearedAfter))!;
        SectionCatalog.RegisterDirect("test.conflict.cleared", target);

        HarmonyMethod prefix = new(typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ForeignPrefix))!);
        _foreignHarmony.Patch(target, prefix: prefix);
        HarmonyConflictRecorder.RecordConflicts(_ourHarmony);
        HarmonyConflictRecorder.Count.Should().BeGreaterOrEqualTo(1);

        HarmonyConflictRecorder.Clear();

        HarmonyConflictRecorder.Count.Should().Be(0);
        HarmonyConflictRecorder.Conflicts.Should().BeEmpty();
    }

    public static class ConflictTargets {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Tracked() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OwnedByUs() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ClearedAfter() { }

        public static void ForeignPrefix() { }

        public static void OurPrefix() { }
    }
}
