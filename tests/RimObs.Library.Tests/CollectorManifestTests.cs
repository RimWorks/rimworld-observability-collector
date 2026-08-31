using System;
using System.IO;
using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorManifestTests {
    private const string FullManifest = """
        {
          "schema_version": 1,
          "version": "1.4.2",
          "git_sha": "abc123def456",
          "git_branch": "main",
          "built_at": "2026-05-19T14:32:18Z",
          "builder": "github-actions",
          "ci_run_id": "9876543210",
          "dotnet_runtime": "net10.0",
          "host_os": "win-x64",
          "library_schema_compat": { "min": 1, "max": 3 },
          "features": ["udp_telemetry", "messagepack_wire"]
        }
        """;

    [Fact]
    public void TryParse_reads_version_and_schema_compat_from_full_manifest() {
        CollectorManifest? manifest = CollectorManifest.TryParse(FullManifest);

        manifest.Should().NotBeNull();
        manifest!.SchemaVersion.Should().Be(1);
        manifest.Version.Should().Be("1.4.2");
        manifest.LibrarySchemaCompat.Should().NotBeNull();
        manifest.LibrarySchemaCompat!.Min.Should().Be(1);
        manifest.LibrarySchemaCompat.Max.Should().Be(3);
    }

    [Fact]
    public void TryParse_tolerates_missing_fields() {
        CollectorManifest? manifest = CollectorManifest.TryParse("""{ "version": "2.0.0" }""");

        manifest.Should().NotBeNull();
        manifest!.Version.Should().Be("2.0.0");
        manifest.LibrarySchemaCompat.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    public void TryParse_returns_null_for_empty_or_malformed(string json) {
        CollectorManifest.TryParse(json).Should().BeNull();
    }

    [Fact]
    public void TryReadFile_returns_null_for_missing_path() {
        CollectorManifest.TryReadFile(Path.Combine(Path.GetTempPath(), "rimobs-does-not-exist.version")).Should().BeNull();
    }

    [Fact]
    public void TryReadFile_round_trips_a_real_file() {
        string path = Path.Combine(Path.GetTempPath(), $"rimobs-manifest-{Guid.NewGuid():N}.version");
        File.WriteAllText(path, FullManifest);
        try {
            CollectorManifest? manifest = CollectorManifest.TryReadFile(path);

            manifest.Should().NotBeNull();
            manifest!.Version.Should().Be("1.4.2");
        }
        finally {
            File.Delete(path);
        }
    }
}
