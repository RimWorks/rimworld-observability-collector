using System;
using System.IO;
using System.Linq;
using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorScannerTests {
    [Fact]
    public void Scan_returns_empty_when_root_missing_or_blank() {
        CollectorScanner.Scan("").Should().BeEmpty();
        CollectorScanner.Scan(Path.Combine(Path.GetTempPath(), "rimobs-no-such-dir-" + Guid.NewGuid().ToString("N"))).Should().BeEmpty();
    }

    [Fact]
    public void Scan_finds_candidate_in_well_formed_mod() {
        using TempDirectory root = new TempDirectory();
        WriteMod(root.Path, "alpha", "1.2.3", windows: true);

        var candidates = CollectorScanner.Scan(root.Path);

        candidates.Should().HaveCount(1);
        candidates[0].Version.Should().Be(new Version(1, 2, 3));
        candidates[0].ExecutablePath.Should().EndWith("Collector.exe");
    }

    [Fact]
    public void Scan_skips_mods_without_manifest_or_executable() {
        using TempDirectory root = new TempDirectory();
        WriteMod(root.Path, "good", "2.0.0", windows: true);

        string missingManifest = Path.Combine(root.Path, "no-manifest", "Collector");
        Directory.CreateDirectory(missingManifest);
        File.WriteAllText(Path.Combine(missingManifest, "Collector.exe"), "binary");

        string missingExe = Path.Combine(root.Path, "no-exe", "Collector");
        Directory.CreateDirectory(missingExe);
        File.WriteAllText(Path.Combine(missingExe, "Collector.version"), Manifest("1.5.0"));

        var candidates = CollectorScanner.Scan(root.Path);

        candidates.Should().HaveCount(1);
        candidates[0].Version.Should().Be(new Version(2, 0, 0));
    }

    [Fact]
    public void Scan_accepts_unix_executable_without_exe_extension() {
        using TempDirectory root = new TempDirectory();
        WriteMod(root.Path, "unix-mod", "3.1.0", windows: false);

        var candidates = CollectorScanner.Scan(root.Path);

        candidates.Should().HaveCount(1);
        candidates[0].ExecutablePath.Should().EndWith("Collector");
        candidates[0].ExecutablePath.Should().NotEndWith(".exe");
    }

    [Fact]
    public void Scan_skips_mods_with_corrupt_manifest() {
        using TempDirectory root = new TempDirectory();
        string badDir = Path.Combine(root.Path, "broken", "Collector");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "Collector.exe"), "binary");
        File.WriteAllText(Path.Combine(badDir, "Collector.version"), "{ not json");

        CollectorScanner.Scan(root.Path).Should().BeEmpty();
    }

    [Fact]
    public void Scan_then_select_highest_picks_newest_across_mods() {
        using TempDirectory root = new TempDirectory();
        WriteMod(root.Path, "old", "1.0.0", windows: true);
        WriteMod(root.Path, "newest", "2.5.0", windows: true);
        WriteMod(root.Path, "newest-but-beta", "2.5.1-beta.1", windows: true);

        var candidates = CollectorScanner.Scan(root.Path);
        CollectorCandidate? best = CollectorDiscovery.SelectHighest(candidates);

        best.Should().NotBeNull();
        best!.Version.Should().Be(new Version(2, 5, 1));
        best.IsPrerelease.Should().BeTrue();
        candidates.Select(c => c.Version).Should().BeEquivalentTo(
            new[] { new Version(1, 0, 0), new Version(2, 5, 0), new Version(2, 5, 1) });
    }

    [Fact]
    public void Scan_finds_candidate_under_rid_subfolder() {
        using TempDirectory root = new TempDirectory();
        WriteModWithRid(root.Path, "linux-mod", "linux-x64", "4.2.0", windows: false);

        var candidates = CollectorScanner.Scan(root.Path);

        candidates.Should().HaveCount(1);
        candidates[0].Version.Should().Be(new Version(4, 2, 0));
        candidates[0].ExecutablePath.Should().Contain("linux-x64");
    }

    [Fact]
    public void Scan_picks_highest_across_multiple_rid_subfolders() {
        using TempDirectory root = new TempDirectory();
        WriteModWithRid(root.Path, "multi", "linux-x64", "1.0.0", windows: false);
        WriteModWithRid(root.Path, "multi", "win-x64", "1.0.0", windows: true);

        var candidates = CollectorScanner.Scan(root.Path);

        candidates.Should().HaveCount(2);
        CollectorDiscovery.SelectHighest(candidates)!.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public void Scan_ignores_collector_under_assemblies_tree() {
        // RimWorld force-loads everything under Assemblies/ into Mono and the net10 collector
        // crashes it. The collector under Assemblies/ must be invisible to discovery so we never
        // ship there by accident.
        using TempDirectory root = new TempDirectory();
        string buried = Path.Combine(root.Path, "legacy", "Assemblies", "Collector");
        Directory.CreateDirectory(buried);
        File.WriteAllText(Path.Combine(buried, "Collector.version"), Manifest("9.9.9"));
        File.WriteAllText(Path.Combine(buried, "Collector"), "binary");

        CollectorScanner.Scan(root.Path).Should().BeEmpty();
    }

    private static void WriteMod(string root, string modId, string version, bool windows) {
        string collectorDir = Path.Combine(root, modId, "Collector");
        Directory.CreateDirectory(collectorDir);
        File.WriteAllText(Path.Combine(collectorDir, "Collector.version"), Manifest(version));
        string exeName = windows ? "Collector.exe" : "Collector";
        File.WriteAllText(Path.Combine(collectorDir, exeName), "binary");
    }

    private static void WriteModWithRid(string root, string modId, string rid, string version, bool windows) {
        string ridDir = Path.Combine(root, modId, "Collector", rid);
        Directory.CreateDirectory(ridDir);
        File.WriteAllText(Path.Combine(ridDir, "Collector.version"), Manifest(version));
        string exeName = windows ? "Collector.exe" : "Collector";
        File.WriteAllText(Path.Combine(ridDir, exeName), "binary");
    }

    private static string Manifest(string version) =>
        $$"""
        {
          "schema_version": 1,
          "version": "{{version}}",
          "library_schema_compat": { "min": 1, "max": 3 }
        }
        """;

    private sealed class TempDirectory : IDisposable {
        public TempDirectory() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rimobs-scan-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() {
            try {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException) {
            }
        }
    }
}
