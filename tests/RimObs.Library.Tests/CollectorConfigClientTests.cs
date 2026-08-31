using RimWorks.RimObs.Config;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorConfigClientTests {
    public CollectorConfigClientTests() {
        SectionRegistry.Clear();
    }

    [Fact]
    public void ApplyToRegistry_disables_named_sections_and_leaves_others_active() {
        SectionHandle tick = SectionRegistry.Register("core.tick");
        SectionHandle path = SectionRegistry.Register("core.path");
        SectionHandle ui = SectionRegistry.Register("core.ui");

        CollectorConfigDocument document = CollectorConfigDocument.TryParse(
            """{ "schema_version": 1, "sections": { "disabled": ["core.tick", "core.path"] } }"""
        )!;
        CollectorConfigClient.ApplyToRegistry(document);

        tick.IsActive().Should().BeFalse();
        path.IsActive().Should().BeFalse();
        ui.IsActive().Should().BeTrue();
    }

    [Fact]
    public void ApplyToRegistry_re_enables_sections_removed_from_disabled_list() {
        SectionHandle tick = SectionRegistry.Register("core.tick");

        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse("""{ "sections": { "disabled": ["core.tick"] } }""")!
        );
        tick.IsActive().Should().BeFalse();

        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse("""{ "sections": { "disabled": [] } }""")!
        );
        tick.IsActive().Should().BeTrue();
    }

    [Fact]
    public void ApplyToRegistry_with_no_sections_block_enables_all() {
        SectionHandle tick = SectionRegistry.Register("core.tick");
        SectionRegistry.SetActive(tick.Id, false);

        CollectorConfigClient.ApplyToRegistry(CollectorConfigDocument.TryParse("""{ "schema_version": 1 }""")!);

        tick.IsActive().Should().BeTrue();
    }
}
