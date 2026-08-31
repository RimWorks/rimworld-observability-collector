using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Bundle;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleImportServiceTests : IDisposable {
    private static readonly string[] ExpectedContents = ["manifest.json", "session_summary.json"];
    private readonly string _baseDir;
    private readonly BundleImportRegistry _registry;

    public BundleImportServiceTests() {
        _baseDir = Path.Combine(Path.GetTempPath(), $"rimobs-import-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
        _registry = new BundleImportRegistry(_baseDir, TimeSpan.FromMinutes(30));
    }

    public void Dispose() {
        _registry.RemoveAll();
        if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static MemoryStream BuildSampleBundle() {
        MemoryStream ms = new MemoryStream();
        using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
            ZipArchiveEntry manifest = zip.CreateEntry("manifest.json");
            using (Stream s = manifest.Open()) {
                byte[] body = Encoding.UTF8.GetBytes("""
                    {"schema_version":1,"session_id":"x","created_utc":"2026-05-28T00:00:00Z","collector_version":"0.1.0","entries":["session_summary.json"]}
                """);
                s.Write(body);
            }
            ZipArchiveEntry summary = zip.CreateEntry("session_summary.json");
            using (Stream s = summary.Open()) {
                byte[] body = Encoding.UTF8.GetBytes("""{"session_id":"x"}""");
                s.Write(body);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Import_ExtractsAndReturnsToken() {
        BundleImportService service = new BundleImportService(_registry);
        using MemoryStream stream = BuildSampleBundle();

        BundleImportResult result = await service.ImportAsync(stream);

        result.Status.Should().Be(BundleImportStatus.Ok);
        result.Entry.Should().NotBeNull();
        result.Entry!.Contents.Should().BeEquivalentTo(ExpectedContents);
        File.Exists(Path.Combine(result.Entry.TempDir, "manifest.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Import_RejectsMissingManifest() {
        MemoryStream stream = new MemoryStream();
        using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            ZipArchiveEntry entry = zip.CreateEntry("session_summary.json");
            using Stream s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("{}"));
        }
        stream.Position = 0;

        BundleImportService service = new BundleImportService(_registry);
        BundleImportResult result = await service.ImportAsync(stream);

        result.Status.Should().Be(BundleImportStatus.MissingManifest);
        result.Entry.Should().BeNull();
    }

    [Fact]
    public async Task Import_RejectsPathTraversal() {
        MemoryStream stream = new MemoryStream();
        using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            ZipArchiveEntry entry = zip.CreateEntry("../escape.json");
            using Stream s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("{}"));
        }
        stream.Position = 0;

        BundleImportService service = new BundleImportService(_registry);
        BundleImportResult result = await service.ImportAsync(stream);

        result.Status.Should().Be(BundleImportStatus.InvalidArchive);

        string? parent = Path.GetDirectoryName(_baseDir.TrimEnd(Path.DirectorySeparatorChar));
        File.Exists(Path.Combine(_baseDir, "escape.json")).Should().BeFalse(
            "a traversal entry must never be written inside the import root");
        if (parent is not null) {
            File.Exists(Path.Combine(parent, "escape.json")).Should().BeFalse(
                "a traversal entry must never escape the import root");
        }
    }
}
