using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorks.RimObs.Config;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class ProfilingXmlLoaderTests : IDisposable {
    private static readonly string[] ExpectedMethodNames = ["Calculate", "Apply"];
    private static readonly string[] ExpectedTypeNames = ["MyMod.Combat.HitChance", "MyMod.Combat.Damage"];
    private readonly List<string> _tempDirs = new();

    public void Dispose() {
        foreach (string dir in _tempDirs) {
            try {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch {
            }
        }
    }

    private string CreateModDir(string packageId, string profilingXmlContent) {
        string root = Path.Combine(Path.GetTempPath(), $"rimobs-profiling-{packageId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "About"));
        File.WriteAllText(Path.Combine(root, "About", ProfilingXmlLoader.FileName), profilingXmlContent);
        _tempDirs.Add(root);
        return root;
    }

    [Fact]
    public void Empty_mod_list_returns_zero_counts() {
        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(Array.Empty<(string, string)>());

        r.FilesScanned.Should().Be(0);
        r.FilesLoaded.Should().Be(0);
        r.SectionsDeclared.Should().Be(0);
        r.MethodsDeclared.Should().Be(0);
        r.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Missing_profiling_xml_is_silently_skipped() {
        string root = Path.Combine(Path.GetTempPath(), $"rimobs-profiling-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "About"));
        _tempDirs.Add(root);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.empty") });

        r.FilesScanned.Should().Be(0);
        r.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Valid_single_section_registers_methods_with_owner_prefix() {
        const string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Profiling>
            <Section name="combat" subsystem="game">
                <Methods>
                    <Method>MyMod.Combat.HitChance:Calculate</Method>
                    <Method>MyMod.Combat.Damage:Apply</Method>
                </Methods>
            </Section>
        </Profiling>
        """;
        string root = CreateModDir("singlesection", xml);
        int catalogBefore = SectionCatalog.Entries.Count;

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.single") });

        r.FilesScanned.Should().Be(1);
        r.FilesLoaded.Should().Be(1);
        r.SectionsDeclared.Should().Be(1);
        r.MethodsDeclared.Should().Be(2);
        r.Warnings.Should().BeEmpty();

        IReadOnlyList<CatalogEntry> all = SectionCatalog.Entries;
        List<CatalogEntry> mine = all.Skip(catalogBefore).ToList();
        mine.Should().HaveCount(2);
        mine.Should().AllSatisfy(e => {
            e.Name.Should().Be("test.single.combat");
            e.Declared.Should().BeTrue();
            e.Owner.Should().Be("test.single");
            e.Subsystem.Should().Be("game");
        });
        mine.Select(e => e.MethodName).Should().BeEquivalentTo(ExpectedMethodNames);
        mine.Select(e => e.TypeName).Should().BeEquivalentTo(ExpectedTypeNames);
    }

    [Fact]
    public void Multiple_sections_in_one_file_all_register() {
        const string xml = """
        <Profiling>
            <Section name="ai">
                <Methods>
                    <Method>MyMod.AI.Brain:Think</Method>
                </Methods>
            </Section>
            <Section name="rendering">
                <Methods>
                    <Method>MyMod.Rendering.Camera:Render</Method>
                    <Method>MyMod.Rendering.Light:Update</Method>
                </Methods>
            </Section>
        </Profiling>
        """;
        string root = CreateModDir("multisection", xml);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.multi") });

        r.SectionsDeclared.Should().Be(2);
        r.MethodsDeclared.Should().Be(3);
        r.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_mods_each_with_profiling_xml_all_load() {
        const string xmlA = """
        <Profiling>
            <Section name="combat">
                <Methods>
                    <Method>ModA.Hit:Calc</Method>
                </Methods>
            </Section>
        </Profiling>
        """;
        const string xmlB = """
        <Profiling>
            <Section name="economy">
                <Methods>
                    <Method>ModB.Money:Apply</Method>
                </Methods>
            </Section>
        </Profiling>
        """;
        string rootA = CreateModDir("modA", xmlA);
        string rootB = CreateModDir("modB", xmlB);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[]
        {
            (rootA, "author.moda"),
            (rootB, "author.modb"),
        });

        r.FilesScanned.Should().Be(2);
        r.FilesLoaded.Should().Be(2);
        r.SectionsDeclared.Should().Be(2);
        r.MethodsDeclared.Should().Be(2);
    }

    [Fact]
    public void Wrong_root_element_warns_and_loads_nothing() {
        const string xml = """
        <NotProfiling>
            <Section name="x">
                <Methods><Method>A:B</Method></Methods>
            </Section>
        </NotProfiling>
        """;
        string root = CreateModDir("wrongroot", xml);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.wrongroot") });

        r.FilesScanned.Should().Be(1);
        r.SectionsDeclared.Should().Be(0);
        r.MethodsDeclared.Should().Be(0);
        r.Warnings.Should().ContainSingle(w => w.Contains("root element must be <Profiling>"));
    }

    [Fact]
    public void Section_missing_name_attribute_warns() {
        const string xml = """
        <Profiling>
            <Section>
                <Methods><Method>A:B</Method></Methods>
            </Section>
        </Profiling>
        """;
        string root = CreateModDir("noname", xml);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.noname") });

        r.SectionsDeclared.Should().Be(0);
        r.MethodsDeclared.Should().Be(0);
        r.Warnings.Should().ContainSingle(w => w.Contains("missing 'name'"));
    }

    [Fact]
    public void Section_missing_methods_container_warns() {
        const string xml = """
        <Profiling>
            <Section name="orphan"/>
        </Profiling>
        """;
        string root = CreateModDir("nomethods", xml);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.nomethods") });

        r.SectionsDeclared.Should().Be(1);
        r.MethodsDeclared.Should().Be(0);
        r.Warnings.Should().ContainSingle(w => w.Contains("missing <Methods>"));
    }

    [Fact]
    public void Invalid_method_spec_warns_but_continues() {
        const string xml = """
        <Profiling>
            <Section name="mixed">
                <Methods>
                    <Method>NoColon</Method>
                    <Method>:NoType</Method>
                    <Method>NoMethod:</Method>
                    <Method></Method>
                    <Method>Valid.Type:Method</Method>
                </Methods>
            </Section>
        </Profiling>
        """;
        string root = CreateModDir("invalidspecs", xml);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.invalid") });

        r.SectionsDeclared.Should().Be(1);
        r.MethodsDeclared.Should().Be(1);
        r.Warnings.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Malformed_xml_warns_and_skips_file() {
        string root = CreateModDir("malformed", "<Profiling><not closed>");

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[] { (root, "test.malformed") });

        r.FilesScanned.Should().Be(1);
        r.FilesLoaded.Should().Be(0);
        r.Warnings.Should().ContainSingle(w => w.Contains("failed to parse"));
    }

    [Fact]
    public void Empty_or_null_package_id_skips_mod() {
        const string xml = """
        <Profiling>
            <Section name="x">
                <Methods><Method>A:B</Method></Methods>
            </Section>
        </Profiling>
        """;
        string root = CreateModDir("emptypkg", xml);

        ProfilingXmlLoader.LoadResult r = ProfilingXmlLoader.LoadFromMods(new[]
        {
            (root, ""),
            ("", "test.norootdir"),
        });

        r.FilesScanned.Should().Be(0);
        r.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Method_spec_with_nested_namespace_splits_on_last_colon() {
        ProfilingXmlLoader.TrySplitMethodSpec("A.B.C:Method", out string typeA, out string methodA).Should().BeTrue();
        typeA.Should().Be("A.B.C");
        methodA.Should().Be("Method");

        ProfilingXmlLoader.TrySplitMethodSpec("Generic`1[[X]]:Do", out string typeB, out string methodB).Should().BeTrue();
        typeB.Should().Be("Generic`1[[X]]");
        methodB.Should().Be("Do");
    }

    [Fact]
    public void Null_mods_argument_throws() {
        Action act = () => ProfilingXmlLoader.LoadFromMods(null!);
        act.Should().Throw<ArgumentNullException>();
    }


    [Fact]
    public void LoadFromMods_SubsystemAttribute_PropagatesToRegistry() {
        SectionCatalog.Clear();
        SectionRegistry.Clear();

        const string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Profiling>
            <Section name="jobs" subsystem="jobs">
                <Methods>
                    <Method>System.String:IsNullOrEmpty</Method>
                </Methods>
            </Section>
        </Profiling>
        """;

        string dir = CreateModDir("subsystest", xml);

        ProfilingXmlLoader.LoadResult result = ProfilingXmlLoader.LoadFromMods(
            new[] { (dir, "test.subsysmod") });
        SectionCatalog.ResolveAll();

        result.SectionsDeclared.Should().Be(1);
        result.MethodsDeclared.Should().Be(1);

        CatalogEntry? entryNullable = SectionCatalog.Entries.FirstOrDefault(e => e.Name == "test.subsysmod.jobs");
        entryNullable.Should().NotBeNull();
        CatalogEntry entry = entryNullable!;
        entry.Subsystem.Should().Be("jobs");
        entry.SectionId.Should().BeGreaterOrEqualTo(0);
        SectionRegistry.GetSubsystem(entry.SectionId).Should().Be("jobs");
    }
}
