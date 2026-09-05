using System;
using System.Linq;
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

    // regression: 1.6 batches pathfinding through Unity jobs, so a pack carrying only
    // FindPathNow reads near-zero while pawns are moving.
    [Fact]
    public void CorePack_MeasuresPathFinderTick_NotJustSynchronousFindPathNow() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();

        SectionCatalog.RegisterCorePack();

        SectionCatalog.Entries.Should().Contain(
            e => e.TypeName == "Verse.PathFinder" && e.MethodName == "PathFinderTick");
    }

    // duplicate names collapse onto one section id in SectionRegistry.Register, which
    // silently sums the two timings together.
    [Fact]
    public void CorePack_SectionNamesAreUnique() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();

        SectionCatalog.RegisterCorePack();

        SectionCatalog.Entries.Select(e => e.Name).Should().OnlyHaveUniqueItems();
    }
}
