using System.Reflection;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Tests;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using RimObsTest.Fixtures;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Control;

public class PatchRegistryTests : IDisposable {
    public PatchRegistryTests() {
        TestBackend.Activate();
        PatchInstaller.ResetForTests();
        PatchRegistry.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
    }

    public void Dispose() {
        PatchRegistry.ResetForTests();
        PatchInstaller.ResetForTests();
        TestBackend.Deactivate();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        System.GC.SuppressFinalize(this);
    }

    [Fact]
    public void Apply_registers_section_and_records_patch_id() {
        MethodInfo target = typeof(ResolverTargets).GetMethod(
            "Add", [typeof(int), typeof(int)])!;

        ApplyResult applied = PatchRegistry.Apply("test.pkg", target, "ResolverTargets:Add(Int32,Int32)");

        applied.Status.Should().Be(PatchStatus.Active);
        applied.PatchId.Should().BeGreaterThan(0);
        applied.SectionName.Should().StartWith("test.pkg.dynamic.");
        SectionRegistry.Count.Should().Be(1);
    }

    [Fact]
    public void Apply_is_idempotent_for_same_signature() {
        MethodInfo target = typeof(ResolverTargets).GetMethod(
            "Add", [typeof(int), typeof(int)])!;

        ApplyResult first = PatchRegistry.Apply("test.pkg", target, "ResolverTargets:Add(Int32,Int32)");
        ApplyResult second = PatchRegistry.Apply("test.pkg", target, "ResolverTargets:Add(Int32,Int32)");

        second.PatchId.Should().Be(first.PatchId);
        SectionRegistry.Count.Should().Be(1);
    }

    // regression: without a backend the patch cannot be installed, so reporting it Active
    // would tell the dashboard a lie.
    [Fact]
    public void Apply_refuses_without_a_backend() {
        MethodInfo target = typeof(ResolverTargets).GetMethod(
            "Add", [typeof(int), typeof(int)])!;
        PatchBackends.ResetForTests();

        ApplyResult applied = PatchRegistry.Apply("test.pkg", target, "ResolverTargets:Add(Int32,Int32)");

        applied.Status.Should().Be(PatchStatus.Refused);
        applied.ErrorReason.Should().Be("no patching backend loaded");
    }

    [Fact]
    public void Remove_unpatches_and_deactivates_section() {
        MethodInfo target = typeof(ResolverTargets).GetMethod(
            "Add", [typeof(int), typeof(int)])!;
        ApplyResult applied = PatchRegistry.Apply("test.pkg", target, "ResolverTargets:Add(Int32,Int32)");

        bool removed = PatchRegistry.Remove(applied.PatchId);

        removed.Should().BeTrue();
        SectionRegistry.IsActive(applied.SectionId).Should().BeFalse();
    }
}
