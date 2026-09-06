using System.Collections.Generic;
using System.Threading;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class ProfilerNodeIdTests {
    [Fact]
    public void One_section_gets_a_node_id_and_no_parent_node() {
        SectionHandle section = SectionRegistry.Register("nodeid-single");
        SectionRegistry.SetActive(section.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long token = Profiler.Start(section);
            Profiler.Stop(section, token);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().ContainSingle();
        sink.Samples[0].NodeId.Should().NotBe(Profiler.NoParent);
        sink.Samples[0].ParentNodeId.Should().Be(Profiler.NoParent);
    }

    [Fact]
    public void Nested_child_reports_parent_node_id_as_the_parents_node_id() {
        SectionHandle parent = SectionRegistry.Register("nodeid-parent");
        SectionHandle child = SectionRegistry.Register("nodeid-child");
        SectionRegistry.SetActive(parent.Id, true);
        SectionRegistry.SetActive(child.Id, true);

        RecordingSink sink = new();
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

        sink.Samples.Should().HaveCount(2);
        Sample childSample = sink.Samples[0];
        Sample parentSample = sink.Samples[1];
        childSample.ParentNodeId.Should().Be(parentSample.NodeId);
    }

    [Fact]
    public void Three_sequential_siblings_get_distinct_node_ids_under_the_same_parent() {
        SectionHandle parent = SectionRegistry.Register("nodeid-siblings-parent");
        SectionHandle child = SectionRegistry.Register("nodeid-siblings-child");
        SectionRegistry.SetActive(parent.Id, true);
        SectionRegistry.SetActive(child.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long outer = Profiler.Start(parent);
            for (int i = 0; i < 3; i++) {
                long inner = Profiler.Start(child);
                Profiler.Stop(child, inner);
            }
            Profiler.Stop(parent, outer);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().HaveCount(4);
        Sample parentSample = sink.Samples[3];
        int[] childNodeIds = [sink.Samples[0].NodeId, sink.Samples[1].NodeId, sink.Samples[2].NodeId];
        childNodeIds.Should().OnlyHaveUniqueItems();
        sink.Samples[0].ParentNodeId.Should().Be(parentSample.NodeId);
        sink.Samples[1].ParentNodeId.Should().Be(parentSample.NodeId);
        sink.Samples[2].ParentNodeId.Should().Be(parentSample.NodeId);
    }

    [Fact]
    public void Two_sequential_parents_each_attribute_their_child_to_themselves() {
        SectionHandle parent = SectionRegistry.Register("nodeid-two-parents-parent");
        SectionHandle child = SectionRegistry.Register("nodeid-two-parents-child");
        SectionRegistry.SetActive(parent.Id, true);
        SectionRegistry.SetActive(child.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            for (int i = 0; i < 2; i++) {
                long outer = Profiler.Start(parent);
                long inner = Profiler.Start(child);
                Profiler.Stop(child, inner);
                Profiler.Stop(parent, outer);
            }
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().HaveCount(4);
        sink.Samples[0].ParentNodeId.Should().Be(sink.Samples[1].NodeId);
        sink.Samples[2].ParentNodeId.Should().Be(sink.Samples[3].NodeId);
        sink.Samples[1].NodeId.Should().NotBe(sink.Samples[3].NodeId);
    }

    [Fact]
    public void Section_nested_inside_itself_gets_two_different_node_ids() {
        SectionHandle section = SectionRegistry.Register("nodeid-recursive");
        SectionRegistry.SetActive(section.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long outer = Profiler.Start(section);
            long inner = Profiler.Start(section);
            Profiler.Stop(section, inner);
            Profiler.Stop(section, outer);
        }
        finally {
            Profiler.SetSink(null);
        }

        sink.Samples.Should().HaveCount(2);
        sink.Samples[0].NodeId.Should().NotBe(sink.Samples[1].NodeId);
        sink.Samples[0].ParentNodeId.Should().Be(sink.Samples[1].NodeId);
    }

    [Fact]
    public void Node_ids_keep_climbing_across_frames_and_never_reset() {
        SectionHandle section = SectionRegistry.Register("nodeid-across-frames");
        SectionRegistry.SetActive(section.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            long firstToken = Profiler.Start(section);
            Profiler.Stop(section, firstToken);
            int firstNodeId = sink.Samples[0].NodeId;

            for (int frame = 0; frame < 5; frame++) {
                long token = Profiler.Start(section);
                Profiler.Stop(section, token);
            }

            sink.Samples[^1].NodeId.Should().BeGreaterThan(firstNodeId);
        }
        finally {
            Profiler.SetSink(null);
        }
    }

    [Fact]
    public void Two_threads_profiling_at_once_emit_no_shared_node_id() {
        SectionHandle section = SectionRegistry.Register("nodeid-cross-thread");
        SectionRegistry.SetActive(section.Id, true);

        RecordingSink sink = new();
        Profiler.SetSink(sink);
        try {
            const int perThread = 500;
            void Run() {
                for (int i = 0; i < perThread; i++) {
                    long token = Profiler.Start(section);
                    Profiler.Stop(section, token);
                }
            }

            Thread t1 = new(Run);
            Thread t2 = new(Run);
            t1.Start();
            t2.Start();
            t1.Join();
            t2.Join();

            HashSet<int> nodeIds = new();
            lock (sink.Samples) {
                foreach (Sample sample in sink.Samples)
                    nodeIds.Add(sample.NodeId);
            }

            nodeIds.Should().HaveCount(perThread * 2);
        }
        finally {
            Profiler.SetSink(null);
        }
    }
}
