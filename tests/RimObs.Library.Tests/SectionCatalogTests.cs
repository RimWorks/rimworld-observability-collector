using System;
using System.Reflection;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class SectionCatalogTests {
    [Fact]
    public void RegisterDirect_AcceptsSubsystemAndSetsOnEntryAndRegistry() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        MethodBase target = typeof(SectionCatalogTests).GetMethod(
            nameof(RegisterDirect_AcceptsSubsystemAndSetsOnEntryAndRegistry))!;

        CatalogEntry entry = SectionCatalog.RegisterDirect("test.direct", target, subsystem: "ui");

        entry.Subsystem.Should().Be("ui");
        SectionRegistry.GetSubsystem(entry.SectionId).Should().Be("ui");
    }


    [Fact]
    public void Register_AcceptsSubsystem_ThreadsToRegistryAfterResolve() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();

        CatalogEntry entry = SectionCatalog.Register(
            name: "test.lazy",
            typeName: typeof(string).FullName!,
            methodName: nameof(string.IsNullOrEmpty),
            paramTypeNames: null,
            subsystem: "strings");
        SectionCatalog.ResolveAll();

        entry.Subsystem.Should().Be("strings");
        entry.SectionId.Should().BeGreaterOrEqualTo(0);
        SectionRegistry.GetSubsystem(entry.SectionId).Should().Be("strings");
    }
}
