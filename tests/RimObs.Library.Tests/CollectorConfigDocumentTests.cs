using RimWorks.RimObs.Config;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorConfigDocumentTests {
    private const string FullConfig = """
        {
          "schema_version": 1,
          "collector": { "listen_address": "127.0.0.1", "port": 17654 },
          "sections": { "disabled": ["core.tick", "core.path"] },
          "storage": { "sqlite_journal_mode": "WAL" }
        }
        """;

    [Fact]
    public void TryParse_reads_schema_version_and_disabled_sections() {
        CollectorConfigDocument? document = CollectorConfigDocument.TryParse(FullConfig);

        document.Should().NotBeNull();
        document!.SchemaVersion.Should().Be(1);
        document.Sections.Should().NotBeNull();
        document.Sections!.Disabled.Should().Equal("core.tick", "core.path");
    }

    [Fact]
    public void TryParse_ignores_unknown_blocks() {
        CollectorConfigDocument? document = CollectorConfigDocument.TryParse(
            """{ "schema_version": 1, "exporters": { "prometheus_port": 7879 } }"""
        );

        document.Should().NotBeNull();
        document!.SchemaVersion.Should().Be(1);
        document.Sections.Should().BeNull();
    }

    [Fact]
    public void TryParse_tolerates_missing_sections_block() {
        CollectorConfigDocument? document = CollectorConfigDocument.TryParse("""{ "schema_version": 1 }""");

        document.Should().NotBeNull();
        document!.Sections.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    public void TryParse_returns_null_for_empty_or_malformed(string json) {
        CollectorConfigDocument.TryParse(json).Should().BeNull();
    }
}
