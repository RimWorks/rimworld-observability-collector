using System;
using System.IO;
using RimWorks.RimObs.Collector.Bundle;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleImportRegistryTests : IDisposable {
    private static readonly string[] ManifestOnly = ["manifest.json"];
    private readonly string _baseDir;

    public BundleImportRegistryTests() {
        _baseDir = Path.Combine(Path.GetTempPath(), $"rimobs-import-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose() {
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Register_ReturnsToken_WithDirectoryUnderBase() {
        BundleImportRegistry registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30));
        BundleImportEntry entry = registry.Register(ManifestOnly);

        entry.Token.Should().NotBeNullOrEmpty();
        entry.TempDir.Should().StartWith(_baseDir);
        Directory.Exists(entry.TempDir).Should().BeTrue();
    }

    [Fact]
    public void TryGet_ReturnsRegisteredEntry() {
        BundleImportRegistry registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30));
        BundleImportEntry entry = registry.Register(ManifestOnly);

        registry.TryGet(entry.Token, out BundleImportEntry? found).Should().BeTrue();
        found!.Token.Should().Be(entry.Token);
    }

    [Fact]
    public void Remove_DeletesTempDir() {
        BundleImportRegistry registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30));
        BundleImportEntry entry = registry.Register(ManifestOnly);
        File.WriteAllText(Path.Combine(entry.TempDir, "manifest.json"), "{}");

        registry.Remove(entry.Token).Should().BeTrue();
        Directory.Exists(entry.TempDir).Should().BeFalse();
        registry.TryGet(entry.Token, out _).Should().BeFalse();
    }

    [Fact]
    public void SweepIdle_RemovesEntriesPastTimeout() {
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        BundleImportRegistry registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30), () => now);
        BundleImportEntry entry = registry.Register(ManifestOnly);
        now = now.AddMinutes(31);

        int removed = registry.SweepIdle();

        removed.Should().Be(1);
        Directory.Exists(entry.TempDir).Should().BeFalse();
    }

    [Fact]
    public void Touch_ResetsLastAccess() {
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        BundleImportRegistry registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30), () => now);
        BundleImportEntry entry = registry.Register(ManifestOnly);
        now = now.AddMinutes(20);
        registry.Touch(entry.Token);
        now = now.AddMinutes(20);

        int removed = registry.SweepIdle();

        removed.Should().Be(0);
        Directory.Exists(entry.TempDir).Should().BeTrue();
    }

    [Fact]
    public void RemoveAll_WipesEverything() {
        BundleImportRegistry registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30));
        BundleImportEntry a = registry.Register(ManifestOnly);
        BundleImportEntry b = registry.Register(ManifestOnly);

        registry.RemoveAll();

        Directory.Exists(a.TempDir).Should().BeFalse();
        Directory.Exists(b.TempDir).Should().BeFalse();
        registry.TryGet(a.Token, out _).Should().BeFalse();
        registry.TryGet(b.Token, out _).Should().BeFalse();
    }
}
