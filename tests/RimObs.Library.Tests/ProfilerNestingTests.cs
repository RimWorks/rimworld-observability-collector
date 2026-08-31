using System.Collections.Generic;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class ProfilerNestingTests {
    private sealed class CapturingSink : ISampleSink {
        public readonly List<(int SectionId, int ParentId)> Records = new();

        public void RecordSection(int sectionId, int parentId, long startTimestamp, long elapsedTicks) {
            Records.Add((sectionId, parentId));
        }
    }

    [Fact]
    public void Nested_section_records_enclosing_section_as_parent() {
        SectionHandle parent = SectionRegistry.Register("nesting-parent");
        SectionHandle child = SectionRegistry.Register("nesting-child");
        SectionRegistry.SetActive(parent.Id, true);
        SectionRegistry.SetActive(child.Id, true);

        CapturingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long outer = Profiler.Start(parent);
            long inner = Profiler.Start(child);
            Profiler.Stop(child, inner);
            Profiler.Stop(parent, outer);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Records.Should().HaveCount(2);
        sink.Records[0].Should().Be((child.Id, parent.Id));
        sink.Records[1].Should().Be((parent.Id, Profiler.NoParent));
    }

    [Fact]
    public void Top_level_section_records_no_parent() {
        SectionHandle section = SectionRegistry.Register("nesting-top-level");
        SectionRegistry.SetActive(section.Id, true);

        CapturingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long token = Profiler.Start(section);
            Profiler.Stop(section, token);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Records.Should().ContainSingle();
        sink.Records[0].Should().Be((section.Id, Profiler.NoParent));
    }
}
