using RimWorks.RimObs.Auto;
using RimWorks.RimObs.Config;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Transport;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorConfigClientTests {
    public CollectorConfigClientTests() {
        SectionRegistry.Clear();
        AutoInstrumentRunner.ResetForTests();
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
    public void ApplyToRegistry_leaves_auto_muted_sections_muted() {
        SectionHandle leaf = SectionRegistry.Register("core.leaf");
        SectionRegistry.MuteAuto(leaf.Id);

        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse("""{ "sections": { "disabled": [] } }""")!
        );

        leaf.IsActive().Should().BeFalse();
    }

    [Fact]
    public void ApplyToRegistry_with_no_sections_block_enables_all() {
        SectionHandle tick = SectionRegistry.Register("core.tick");
        SectionRegistry.SetActive(tick.Id, false);

        CollectorConfigClient.ApplyToRegistry(CollectorConfigDocument.TryParse("""{ "schema_version": 1 }""")!);

        tick.IsActive().Should().BeTrue();
    }

    [Fact]
    public void ApplyToRegistry_reads_the_max_capture_depth_the_collector_sent() {
        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse("""{ "sampling": { "max_capture_depth": 16 } }""")!
        );

        Profiler.MaxDepth.Should().Be(16);
    }

    [Fact]
    public void ApplyToRegistry_falls_back_to_the_default_depth_when_sampling_is_absent() {
        Profiler.MaxDepth = 3;

        CollectorConfigClient.ApplyToRegistry(CollectorConfigDocument.TryParse("""{ "schema_version": 1 }""")!);

        Profiler.MaxDepth.Should().Be(Profiler.DefaultMaxDepth);
    }

    // the config poll is the only push for ring_capacity; the collector never calls /ring-capacity.
    [Fact]
    public void ApplyToRegistry_retunes_the_sink_ring_the_collector_sent() {
        using UdpTelemetrySink sink = new(ownerId: "test.pkg", port: 45998);
        ControlServices.SetSink(sink);
        try {
            CollectorConfigClient.ApplyToRegistry(
                CollectorConfigDocument.TryParse("""{ "sampling": { "ring_capacity": 2048 } }""")!);

            sink.RingCapacity.Should().Be(2048);
        }
        finally {
            ControlServices.SetSink(null);
        }
    }

    [Fact]
    public void ApplyToRegistry_parks_the_auto_instrument_settings_for_the_next_frame() {
        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse(
                """
                {
                  "auto_instrument": {
                    "enabled": true,
                    "filters": "Assembly-CSharp!Verse.*",
                    "ignore": "Assembly-CSharp!Verse.Log::*",
                    "mute_trivial": false
                  }
                }
                """)!
        );

        AutoInstrumentRequest.TryTake(out bool enabled, out string filters, out string ignore, out bool mute)
            .Should().BeTrue();
        enabled.Should().BeTrue();
        filters.Should().Be("Assembly-CSharp!Verse.*");
        ignore.Should().Be("Assembly-CSharp!Verse.Log::*");
        mute.Should().BeFalse();
    }

    [Fact]
    public void ApplyToRegistry_sends_empty_filters_when_auto_instrumentation_is_off() {
        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse(
                """{ "auto_instrument": { "enabled": false, "filters": "Assembly-CSharp!Verse.*" } }""")!
        );

        AutoInstrumentRequest.TryTake(out bool enabled, out string filters, out string _, out bool _)
            .Should().BeTrue();
        enabled.Should().BeFalse();
        filters.Should().BeEmpty();
    }

    [Fact]
    public void ApplyToRegistry_reads_the_target_cap_the_collector_sent() {
        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse(
                """{ "auto_instrument": { "enabled": true, "max_targets": 40000 } }""")!
        );

        AutoInstrumentRunner.MaxTargets.Should().Be(40000);
    }

    [Fact]
    public void ApplyToRegistry_falls_back_to_the_default_cap_when_none_was_sent() {
        AutoInstrumentRunner.MaxTargets = 40000;

        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse("""{ "auto_instrument": { "enabled": true } }""")!);

        AutoInstrumentRunner.MaxTargets.Should().Be(AutoInstrumentScanner.DefaultMaxTargets);
    }

    // enabled used to collapse into the filter string, so with no filters configured the off
    // poll looked identical to the on poll and was deduped away. the toggle did nothing.
    [Fact]
    public void Switching_the_toggle_off_with_no_filters_still_queues_a_request() {
        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse(
                """{ "auto_instrument": { "enabled": true, "filters": "" } }""")!);
        AutoInstrumentRequest.TryTake(out bool _, out string _, out string _, out bool _).Should().BeTrue();

        CollectorConfigClient.ApplyToRegistry(
            CollectorConfigDocument.TryParse(
                """{ "auto_instrument": { "enabled": false, "filters": "" } }""")!);

        AutoInstrumentRequest.HasPending.Should().BeTrue();
        AutoInstrumentRequest.TryTake(out bool enabled, out string _, out string _, out bool _).Should().BeTrue();
        enabled.Should().BeFalse();
    }

    [Fact]
    public void An_unchanged_poll_does_not_queue_a_second_rescan() {
        const string json = """{ "auto_instrument": { "enabled": true, "filters": "Verse.*" } }""";

        CollectorConfigClient.ApplyToRegistry(CollectorConfigDocument.TryParse(json)!);
        AutoInstrumentRequest.TryTake(out bool _, out string _, out string _, out bool _).Should().BeTrue();

        CollectorConfigClient.ApplyToRegistry(CollectorConfigDocument.TryParse(json)!);

        AutoInstrumentRequest.HasPending.Should().BeFalse();
    }
}
