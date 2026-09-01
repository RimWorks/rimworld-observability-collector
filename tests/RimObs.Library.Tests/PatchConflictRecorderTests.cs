using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using HarmonyLib;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class PatchConflictRecorderTests : IDisposable {
    private readonly Harmony _foreignHarmony;

    public PatchConflictRecorderTests() {
        TestBackend.Activate();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        PatchConflictRecorder.Clear();
        _foreignHarmony = new Harmony($"foreign.modder.{Guid.NewGuid():N}");
    }

    public void Dispose() {
        try { _foreignHarmony.UnpatchAll(_foreignHarmony.Id); } catch { }
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        PatchConflictRecorder.Clear();
        TestBackend.Deactivate();
    }

    [Fact]
    public void RecordConflicts_records_foreign_patches_on_tracked_section() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.Tracked))!;
        SectionCatalog.RegisterDirect("test.conflict.tracked", target);

        HarmonyMethod prefix = new(typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ForeignPrefix))!);
        _foreignHarmony.Patch(target, prefix: prefix);

        PatchConflictRecorder.RecordConflicts();

        PatchConflictRecorder.Count.Should().BeGreaterOrEqualTo(1);
        PatchConflictRecorder.Conflicts.Should().Contain(c =>
            c.SectionName == "test.conflict.tracked" &&
            c.OtherOwner == _foreignHarmony.Id &&
            c.PatchType == PatchKind.Prefix);
    }

    [Fact]
    public void RecordConflicts_skips_patches_owned_by_us() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.OwnedByUs))!;
        SectionCatalog.RegisterDirect("test.conflict.ours", target);

        PatchBackends.Active!.Patch(target);

        PatchConflictRecorder.RecordConflicts();

        PatchConflictRecorder.Conflicts.Should().NotContain(c => c.SectionName == "test.conflict.ours");
    }

    [Fact]
    public void RecordConflicts_ignores_unresolved_entries() {
        SectionCatalog.Register("test.conflict.unresolved", "NoSuchType.Ever", "Op", null);

        Action act = PatchConflictRecorder.RecordConflicts;

        act.Should().NotThrow();
        PatchConflictRecorder.Conflicts.Should().NotContain(c => c.SectionName == "test.conflict.unresolved");
    }

    // regression: no backend means nothing to ask about conflicts, not a null dereference.
    [Fact]
    public void RecordConflicts_records_nothing_without_a_backend() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.Tracked))!;
        SectionCatalog.RegisterDirect("test.conflict.no_backend", target);
        _foreignHarmony.Patch(target, prefix: new HarmonyMethod(
            typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ForeignPrefix))!));
        PatchBackends.ResetForTests();

        PatchConflictRecorder.RecordConflicts();

        PatchConflictRecorder.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_empties_the_conflict_list() {
        MethodInfo target = typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ClearedAfter))!;
        SectionCatalog.RegisterDirect("test.conflict.cleared", target);

        HarmonyMethod prefix = new(typeof(ConflictTargets).GetMethod(nameof(ConflictTargets.ForeignPrefix))!);
        _foreignHarmony.Patch(target, prefix: prefix);
        PatchConflictRecorder.RecordConflicts();
        PatchConflictRecorder.Count.Should().BeGreaterOrEqualTo(1);

        PatchConflictRecorder.Clear();

        PatchConflictRecorder.Count.Should().Be(0);
        PatchConflictRecorder.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void BuildBatch_marks_conflicts_known_when_the_backend_can_report_them() {
        PatchConflictRecorder.BuildBatch().ConflictsKnown.Should().BeTrue();
    }

    [Fact]
    public void BuildBatch_marks_conflicts_unknown_for_a_backend_that_cannot_report_them() {
        PatchBackends.ResetForTests();
        PatchBackends.Register(new SilentBackend("Silent"), PatchBackends.ConcordPriority);
        PatchBackends.SelectBest(scan: false);

        PatchConflictRecorder.BuildBatch().ConflictsKnown.Should().BeFalse();
    }

    [Fact]
    public void BuildBatch_marks_conflicts_unknown_without_a_backend() {
        PatchBackends.ResetForTests();

        PatchConflictRecorder.BuildBatch().ConflictsKnown.Should().BeFalse();
    }

    // the ctor argument is load-bearing: it keeps ScanForBackends from constructing this fake.
    private sealed class SilentBackend : IPatchBackend {
        public SilentBackend(string name) => Name = name;
        public string Name { get; }
        public bool SupportsConflictReporting => false;
        public void Patch(MethodBase target) { }
        public void PatchPrefix(MethodBase target, MethodInfo prefix) { }
        public void PatchPostfix(MethodBase target, MethodInfo postfix) { }
        public void Unpatch(MethodBase target) { }
        public IReadOnlyList<ForeignPatch> ConflictsFor(MethodBase target) => Array.Empty<ForeignPatch>();
        public void UnpatchAllForTests() { }
    }

    public static class ConflictTargets {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Tracked() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OwnedByUs() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ClearedAfter() { }

        public static void ForeignPrefix() { }
    }
}
