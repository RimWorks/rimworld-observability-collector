using System;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SessionAggregatorTests {
    [Fact]
    public void OnBatchReceived_increments_counters() {
        SessionAggregator agg = new();

        agg.OnBatchReceived(128);
        agg.OnBatchReceived(64);

        agg.TotalBatches.Should().Be(2);
        agg.TotalBytes.Should().Be(192);
        agg.LastBatchUtc.Should().NotBe(default);
    }

    [Fact]
    public void OnSectionBatch_sums_alloc_bytes_into_section_and_edge_stats() {
        SessionAggregator agg = new();

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [5, 5],
            ElapsedTicks = [100L, 200L],
            StartTimestamps = [10L, 20L],
            ParentIds = [-1, -1],
            FrameOrdinals = [1, 1],
            NodeIds = [1, 2],
            ParentNodeIds = [-1, -1],
            AllocBytes = [64L, 128L],
        });

        agg.FindSection(5)!.TotalAllocBytes.Should().Be(192);
        agg.SnapshotCallEdges().Single(e => e.SectionId == 5).TotalAllocBytes.Should().Be(192);
    }

    [Fact]
    public void OnSectionBatch_from_a_producer_without_alloc_bytes_records_zero() {
        SessionAggregator agg = new();

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [6],
            ElapsedTicks = [100L],
            StartTimestamps = [10L],
            ParentIds = [-1],
            FrameOrdinals = [1],
            NodeIds = [1],
            ParentNodeIds = [-1],
        });

        agg.FindSection(6)!.TotalAllocBytes.Should().Be(0);
        agg.FindSection(6)!.TotalElapsedTicks.Should().Be(100);
    }

    [Fact]
    public void OnSectionBatch_carries_alloc_bytes_onto_the_frame_node() {
        SessionAggregator agg = new();

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [7],
            ElapsedTicks = [100L],
            StartTimestamps = [10L],
            ParentIds = [-1],
            FrameOrdinals = [1],
            NodeIds = [1],
            ParentNodeIds = [-1],
            AllocBytes = [512L],
        });

        agg.Frames.FindByOrdinal(1)!.NodeAllocBytes.Should().Equal(512L);
    }

    [Fact]
    public void OnSessionMeta_stores_meta() {
        SessionAggregator agg = new();
        SessionMeta meta = new() {
            SessionId = "test-session",
            StartedUtcTicks = 123456,
            StopwatchFrequency = 10_000_000,
            AnchorTimestamp = 999,
            LibraryVersion = "1.2.3",
            GameVersion = "1.6",
        };

        agg.OnSessionMeta(meta);

        agg.Meta.Should().NotBeNull();
        agg.Meta!.SessionId.Should().Be("test-session");
        agg.Meta.LibraryVersion.Should().Be("1.2.3");
    }


    [Fact]
    public void OnSessionMeta_forwards_to_persister_when_configured() {
        FakePersister persister = new();
        SessionAggregator agg = new(persister);
        SessionMeta meta = new() { SessionId = "persisted", LibraryVersion = "0.1" };

        agg.OnSessionMeta(meta);

        persister.WrittenMetas.Should().ContainSingle();
        persister.WrittenMetas[0].SessionId.Should().Be("persisted");
    }

    [Fact]
    public void OnSessionMeta_without_persister_does_not_throw() {
        SessionAggregator agg = new(persister: null);
        Action act = () => agg.OnSessionMeta(new SessionMeta { SessionId = "x" });
        act.Should().NotThrow();
    }

    private sealed class FakePersister : RimWorks.RimObs.Collector.Storage.ISessionPersister {
        public List<SessionMeta> WrittenMetas { get; } = [];
        public List<(string id, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.SectionStats> sections)> WrittenSections { get; } = [];
        public List<(string id, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.MetricStats> metrics)> WrittenMetrics { get; } = [];
        public List<(string id, RimWorks.RimObs.Collector.Aggregation.GcEventRecord[] events)> WrittenGc { get; } = [];

        public void WriteSessionMeta(SessionMeta meta) => WrittenMetas.Add(meta);
        public void WriteSectionsSnapshot(string sessionId, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.SectionStats> sections) => WrittenSections.Add((sessionId, sections));
        public void WriteMetricsSnapshot(string sessionId, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.MetricStats> metrics) => WrittenMetrics.Add((sessionId, metrics));
        public void ReplaceGcEventsSnapshot(string sessionId, RimWorks.RimObs.Collector.Aggregation.GcEventRecord[] events) => WrittenGc.Add((sessionId, events));
        public List<(string id, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.CallEdgeStats> edges)> WrittenCallTree { get; } = [];
        public void WriteCallTreeSnapshot(string sessionId, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.CallEdgeStats> edges) => WrittenCallTree.Add((sessionId, edges));
        public List<(string id, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.ThreadInfo> threads)> WrittenThreads { get; } = [];
        public void WriteThreadsSnapshot(string sessionId, IReadOnlyCollection<RimWorks.RimObs.Collector.Aggregation.ThreadInfo> threads) => WrittenThreads.Add((sessionId, threads));
        public void Dispose() { }
    }

    [Fact]
    public void OnSectionRegistrations_records_name_and_id() {
        SessionAggregator agg = new();
        SectionRegistrationsBatch batch = new() {
            SectionIds = [1, 2, 3],
            Names = ["alpha", "bravo", "charlie"],
        };

        agg.OnSectionRegistrations(batch);

        agg.SectionCount.Should().Be(3);
        SectionStats[] sections = agg.SnapshotSections().ToArray();
        sections.Should().Contain(s => s.SectionId == 2 && s.Name == "bravo");
    }

    [Fact]
    public void OnSectionRegistrations_tolerates_length_mismatch_by_truncating() {
        SessionAggregator agg = new();
        SectionRegistrationsBatch batch = new() {
            SectionIds = [1, 2, 3],
            Names = ["alpha", "bravo"],
        };

        agg.OnSectionRegistrations(batch);

        agg.SectionCount.Should().Be(2);
    }

    [Fact]
    public void OnSectionBatch_accumulates_sample_stats() {
        SessionAggregator agg = new();
        SectionBatch batch = new() {
            SectionIds = [1, 1, 1],
            StartTimestamps = [100, 200, 300],
            ElapsedTicks = [50, 20, 100],
        };

        agg.OnSectionBatch(batch);

        agg.TotalSamples.Should().Be(3);
        SectionStats stats = agg.SnapshotSections().Single(s => s.SectionId == 1);
        stats.SampleCount.Should().Be(3);
        stats.TotalElapsedTicks.Should().Be(170);
        stats.MinElapsedTicks.Should().Be(20);
        stats.MaxElapsedTicks.Should().Be(100);
        stats.LastStartTimestamp.Should().Be(300);
    }

    [Fact]
    public void OnSectionBatch_accumulates_call_edges_from_parent_ids() {
        SessionAggregator agg = new();
        SectionBatch batch = new() {
            SectionIds = [1, 2, 2],
            ParentIds = [CallTreeBuilder.NoParent, 1, 1],
            StartTimestamps = [100, 110, 200],
            ElapsedTicks = [500, 30, 70],
        };

        agg.OnSectionBatch(batch);

        CallEdgeStats root = agg.SnapshotCallEdges().Single(e => e.SectionId == 1 && e.ParentId == CallTreeBuilder.NoParent);
        root.CallCount.Should().Be(1);
        root.TotalElapsedTicks.Should().Be(500);

        CallEdgeStats childEdge = agg.SnapshotCallEdges().Single(e => e.SectionId == 2 && e.ParentId == 1);
        childEdge.CallCount.Should().Be(2);
        childEdge.TotalElapsedTicks.Should().Be(100);
    }

    [Fact]
    public void OnSectionBatch_defaults_missing_parent_ids_to_no_parent() {
        SessionAggregator agg = new();
        SectionBatch batch = new() {
            SectionIds = [5, 5],
            StartTimestamps = [1, 2],
            ElapsedTicks = [10, 20],
        };

        agg.OnSectionBatch(batch);

        CallEdgeStats edge = agg.SnapshotCallEdges().Single();
        edge.SectionId.Should().Be(5);
        edge.ParentId.Should().Be(CallTreeBuilder.NoParent);
        edge.CallCount.Should().Be(2);
        edge.TotalElapsedTicks.Should().Be(30);
    }

    [Fact]
    public void A_new_session_resets_the_frame_ring_so_ordinals_can_restart() {
        SessionAggregator aggregator = new();
        // long enough that the test reads as a live stream whatever the machine is doing.
        aggregator.Frames.QuietPeriod = TimeSpan.FromMinutes(10);
        aggregator.OnSessionMeta(new SessionMeta { SessionId = "first" });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 10],
            ParentIds = [-1, -1],
            StartTimestamps = [100L, 700L],
            ElapsedTicks = [500L, 400L],
            FrameOrdinals = [5000, 5001],
        });
        // the stream is live, so Latest serves the frame behind the newest.
        aggregator.Frames.Latest()!.CaptureOrdinal.Should().Be(5000);

        aggregator.OnSessionMeta(new SessionMeta { SessionId = "second" });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 10],
            ParentIds = [-1, -1],
            StartTimestamps = [10L, 70L],
            ElapsedTicks = [50L, 40L],
            FrameOrdinals = [1, 2],
        });

        aggregator.Frames.Latest()!.CaptureOrdinal.Should().Be(1);
        aggregator.Frames.LateSamples.Should().Be(0);
    }

    [Fact]
    public void A_repeated_session_meta_heartbeat_does_not_clear_the_frame_ring() {
        SessionAggregator aggregator = new();
        // long enough that the test reads as a live stream whatever the machine is doing.
        aggregator.Frames.QuietPeriod = TimeSpan.FromMinutes(10);
        aggregator.OnSessionMeta(new SessionMeta { SessionId = "same" });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 10],
            ParentIds = [-1, -1],
            StartTimestamps = [100L, 700L],
            ElapsedTicks = [500L, 400L],
            FrameOrdinals = [1, 2],
        });

        aggregator.OnSessionMeta(new SessionMeta { SessionId = "same" });

        aggregator.Frames.Count.Should().Be(2);
        aggregator.Frames.Latest()!.CaptureOrdinal.Should().Be(1);
    }

    [Fact]
    public void OnSectionBatch_files_samples_into_the_frame_ring_by_ordinal() {
        SessionAggregator aggregator = new();
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20, 10],
            ParentIds = [-1, 10, -1],
            StartTimestamps = [100L, 150L, 700L],
            ElapsedTicks = [500L, 200L, 400L],
            FrameOrdinals = [1, 1, 2],
        });

        FrameSnapshot? frame = aggregator.Frames.FindByOrdinal(1);

        frame.Should().NotBeNull();
        frame!.CaptureOrdinal.Should().Be(1);
        frame.NodeCount.Should().Be(2);
    }

    [Fact]
    public void OnSectionBatch_from_a_v4_producer_files_nothing_into_the_frame_ring() {
        SessionAggregator aggregator = new();
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20],
            ParentIds = [-1, 10],
            StartTimestamps = [100L, 150L],
            ElapsedTicks = [500L, 200L],
        });

        aggregator.Frames.Latest().Should().BeNull();
        aggregator.Frames.PreFrameSamples.Should().Be(2);
    }

    [Fact]
    public void OnSectionBatch_updates_min_and_max_across_multiple_batches() {
        SessionAggregator agg = new();
        agg.OnSectionBatch(new() {
            SectionIds = [1],
            StartTimestamps = [100],
            ElapsedTicks = [500],
        });
        agg.OnSectionBatch(new() {
            SectionIds = [1],
            StartTimestamps = [200],
            ElapsedTicks = [10],
        });
        agg.OnSectionBatch(new() {
            SectionIds = [1],
            StartTimestamps = [300],
            ElapsedTicks = [1000],
        });

        SectionStats stats = agg.SnapshotSections().Single(s => s.SectionId == 1);
        stats.MinElapsedTicks.Should().Be(10);
        stats.MaxElapsedTicks.Should().Be(1000);
    }

    [Fact]
    public void OnGcEvents_increments_total_count_by_batch_size() {
        SessionAggregator agg = new();
        GcEventsBatch batch = new() {
            Generations = new byte[] { 0, 1, 2 },
            PauseTypes = new byte[3],
            HeapBefore = new long[3],
            HeapAfter = new long[3],
            DurationMicros = new long[3],
            Ticks = new long[3],
            AllocationRateBytesPerMinute = new long[3],
        };

        agg.OnGcEvents(batch);
        agg.OnGcEvents(batch);

        agg.TotalGcEvents.Should().Be(6);
    }

    [Fact]
    public void OnGcEvents_tolerates_length_mismatch_by_truncating() {
        SessionAggregator agg = new();
        GcEventsBatch batch = new() {
            Generations = new byte[] { 0, 1, 2 },
            PauseTypes = new byte[3],
            HeapBefore = new long[3],
            HeapAfter = new long[3],
            DurationMicros = new long[] { 5 },
            Ticks = new long[3],
            AllocationRateBytesPerMinute = new long[3],
        };

        agg.OnGcEvents(batch);

        agg.TotalGcEvents.Should().Be(1);
    }

    [Fact]
    public void OnGcEvents_captures_frame_ordinal_per_event() {
        SessionAggregator agg = new();
        GcEventsBatch batch = new() {
            Generations = new byte[] { 0, 0 },
            PauseTypes = new byte[2],
            HeapBefore = new long[2],
            HeapAfter = new long[2],
            DurationMicros = new long[2],
            Ticks = new long[] { 1, 2 },
            AllocationRateBytesPerMinute = new long[2],
            FrameOrdinals = new[] { 40, 41 },
        };

        agg.OnGcEvents(batch);

        GcEventRecord[] events = agg.SnapshotGcEvents(10);
        events.Should().Contain(e => e.FrameOrdinal == 40);
        events.Should().Contain(e => e.FrameOrdinal == 41);
    }

    [Fact]
    public void OnGcEvents_defaults_frame_ordinal_to_zero_for_a_pre_v7_batch() {
        SessionAggregator agg = new();
        GcEventsBatch batch = new() {
            Generations = new byte[] { 0 },
            PauseTypes = new byte[1],
            HeapBefore = new long[1],
            HeapAfter = new long[1],
            DurationMicros = new long[1],
            Ticks = new long[1],
            AllocationRateBytesPerMinute = new long[1],
        };

        agg.OnGcEvents(batch);

        agg.SnapshotGcEvents(10).Should().OnlyContain(e => e.FrameOrdinal == 0);
    }

    [Fact]
    public void OnAllocations_increments_total_count_by_window_count() {
        SessionAggregator agg = new();
        AllocationsBatch batch = new() {
            WindowStartTimestamps = new long[] { 1, 2 },
            WindowDurationsMs = new long[2],
            BytesAllocated = new long[2],
            SamplesCount = new long[2],
        };

        agg.OnAllocations(batch);

        agg.TotalAllocations.Should().Be(2);
    }

    [Fact]
    public void OnPatchConflicts_records_conflicts() {
        SessionAggregator agg = new();
        PatchConflictsBatch batch = new() {
            SectionNames = ["core.tick", "core.map"],
            TargetMethods = ["Verse.TickManager:DoSingleTick", "Verse.Map:MapPreTick"],
            OtherOwners = ["Dubs.PerformanceAnalyzer", "Some.OtherMod"],
            PatchTypes = [1, 3],
            Priorities = [400, 0],
            PatchMethods = ["Dubs.Patch:Prefix", "Some.Patch:Transpiler"],
        };

        agg.OnPatchConflicts(batch);

        agg.PatchConflicts.Should().HaveCount(2);
        agg.PatchConflicts.Should().Contain(c =>
            c.SectionName == "core.tick" && c.OtherOwner == "Dubs.PerformanceAnalyzer" && c.PatchType == 1 && c.Priority == 400);
    }

    [Fact]
    public void OnPatchConflicts_carries_the_unknown_flag_so_empty_is_not_read_as_none() {
        SessionAggregator agg = new();

        agg.PatchConflictsKnown.Should().BeTrue();
        agg.OnPatchConflicts(new PatchConflictsBatch { ConflictsKnown = false });

        agg.PatchConflicts.Should().BeEmpty();
        agg.PatchConflictsKnown.Should().BeFalse();
    }

    [Fact]
    public void OnPatchConflicts_tolerates_length_mismatch_by_truncating() {
        SessionAggregator agg = new();
        PatchConflictsBatch batch = new() {
            SectionNames = ["a", "b", "c"],
            TargetMethods = ["t1", "t2"],
            OtherOwners = ["o1", "o2", "o3"],
            PatchTypes = [1, 2, 3],
            Priorities = [0, 0, 0],
            PatchMethods = ["p1", "p2", "p3"],
        };

        agg.OnPatchConflicts(batch);

        agg.PatchConflicts.Should().HaveCount(2);
    }

    [Fact]
    public void OnPatchConflicts_replaces_previous_snapshot() {
        SessionAggregator agg = new();
        agg.OnPatchConflicts(new() {
            SectionNames = ["a"],
            TargetMethods = ["t"],
            OtherOwners = ["o"],
            PatchTypes = [1],
            Priorities = [0],
            PatchMethods = ["p"],
        });
        agg.OnPatchConflicts(new() {
            SectionNames = ["b", "c"],
            TargetMethods = ["t1", "t2"],
            OtherOwners = ["o1", "o2"],
            PatchTypes = [2, 3],
            Priorities = [0, 0],
            PatchMethods = ["p1", "p2"],
        });

        agg.PatchConflicts.Should().HaveCount(2);
        agg.PatchConflicts.Should().Contain(c => c.SectionName == "b");
        agg.PatchConflicts.Should().NotContain(c => c.SectionName == "a");
    }

    [Fact]
    public void OnTpsFps_records_latest_values() {
        SessionAggregator agg = new();
        agg.HasTpsFps.Should().BeFalse();

        agg.OnTpsFps(new TpsFpsBatch { Tps = 59.5, Fps = 144.2, Tick = 9000 });

        agg.HasTpsFps.Should().BeTrue();
        agg.LatestTps.Should().Be(59.5);
        agg.LatestFps.Should().Be(144.2);
        agg.LatestTpsFpsTick.Should().Be(9000);
    }

    [Fact]
    public void OnTpsFps_replaces_previous_values() {
        SessionAggregator agg = new();
        agg.OnTpsFps(new TpsFpsBatch { Tps = 30.0, Fps = 60.0, Tick = 100 });
        agg.OnTpsFps(new TpsFpsBatch { Tps = 60.0, Fps = 120.0, Tick = 200 });

        agg.LatestTps.Should().Be(60.0);
        agg.LatestFps.Should().Be(120.0);
        agg.LatestTpsFpsTick.Should().Be(200);
    }


    [Fact]
    public void OnMetricRegistrations_records_name_kind_and_unit() {
        SessionAggregator agg = new();
        MetricRegistrationsBatch batch = new() {
            MetricIds = [10, 11],
            Names = ["my.mod.frames_drawn", "my.mod.heap_used"],
            Kinds = [0, 1],
            Units = ["count", "bytes"],
        };

        agg.OnMetricRegistrations(batch);

        agg.MetricCount.Should().Be(2);
        MetricStats m10 = agg.SnapshotMetrics().Single(m => m.MetricId == 10);
        m10.Name.Should().Be("my.mod.frames_drawn");
        m10.Kind.Should().Be(MetricKind.Counter);
        m10.Unit.Should().Be("count");
        MetricStats m11 = agg.SnapshotMetrics().Single(m => m.MetricId == 11);
        m11.Name.Should().Be("my.mod.heap_used");
        m11.Kind.Should().Be(MetricKind.Gauge);
        m11.Unit.Should().Be("bytes");
    }

    [Fact]
    public void OnMetrics_accumulates_latest_value_and_total_samples_per_label() {
        SessionAggregator agg = new();
        agg.OnMetricRegistrations(new MetricRegistrationsBatch {
            MetricIds = [10],
            Names = ["my.mod.frames"],
            Kinds = [0],
            Units = ["count"],
        });

        agg.OnMetrics(new MetricsBatch {
            MetricIds = [10, 10],
            LabelCanonicals = ["scene=map", "scene=ui"],
            Kinds = [0, 0],
            Values = [42, 17],
            SampleCounts = [1, 1],
        });
        agg.OnMetrics(new MetricsBatch {
            MetricIds = [10],
            LabelCanonicals = ["scene=map"],
            Kinds = [0],
            Values = [99],
            SampleCounts = [3],
        });

        agg.TotalMetricObservations.Should().Be(3);
        MetricStats stats = agg.SnapshotMetrics().Single();
        MetricLabelStats mapLabel = stats.Labels["scene=map"];
        mapLabel.LatestValue.Should().Be(99);
        mapLabel.TotalSampleCount.Should().Be(4);
        MetricLabelStats uiLabel = stats.Labels["scene=ui"];
        uiLabel.LatestValue.Should().Be(17);
        uiLabel.TotalSampleCount.Should().Be(1);
    }

    [Fact]
    public void OnMetrics_creates_placeholder_metric_when_registration_missing() {
        SessionAggregator agg = new();
        agg.OnMetrics(new MetricsBatch {
            MetricIds = [99],
            LabelCanonicals = [""],
            Kinds = [2],
            Values = [123],
            SampleCounts = [1],
        });

        MetricStats stats = agg.SnapshotMetrics().Single();
        stats.MetricId.Should().Be(99);
        stats.Kind.Should().Be(MetricKind.Histogram);
        stats.Labels[""].LatestValue.Should().Be(123);
    }

    [Fact]
    public void OnSectionBatch_feeds_distribution_with_percentiles() {
        SessionAggregator agg = new();
        long[] elapsed = new long[200];
        for (int i = 0; i < elapsed.Length; i++)
            elapsed[i] = i + 1;
        int[] ids = new int[elapsed.Length];
        long[] starts = new long[elapsed.Length];
        for (int i = 0; i < elapsed.Length; i++) {
            ids[i] = 7;
            starts[i] = i;
        }

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = ids,
            StartTimestamps = starts,
            ElapsedTicks = elapsed,
        });

        SectionStats stats = agg.SnapshotSections().Single(s => s.SectionId == 7);
        PercentileSnapshot snap = stats.Distribution.SnapshotPercentiles();
        snap.P50Ticks.Should().BeInRange(95, 105);
        snap.P95Ticks.Should().BeInRange(185, 200);
        snap.P99Ticks.Should().BeInRange(195, 200);
    }

    [Fact]
    public void FindSection_returns_known_section_and_null_for_unknown() {
        SessionAggregator agg = new();
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [42],
            Names = ["the.section"],
        });

        agg.FindSection(42).Should().NotBeNull();
        agg.FindSection(42)!.Name.Should().Be("the.section");
        agg.FindSection(999).Should().BeNull();
    }

    [Fact]
    public void Section_registered_after_samples_keeps_name_and_sample_counts() {
        SessionAggregator agg = new();
        agg.OnSectionBatch(new() {
            SectionIds = [42],
            StartTimestamps = [100],
            ElapsedTicks = [200],
        });
        agg.OnSectionRegistrations(new() {
            SectionIds = [42],
            Names = ["late.name"],
        });

        SectionStats stats = agg.SnapshotSections().Single(s => s.SectionId == 42);
        stats.Name.Should().Be("late.name");
        stats.SampleCount.Should().Be(1);
        stats.TotalElapsedTicks.Should().Be(200);
    }
}

public sealed class SessionAggregatorSubsystemTests {
    [Fact]
    public void OnSectionRegistrations_propagates_subsystem_when_present() {
        SessionAggregator aggregator = new();
        SectionRegistrationsBatch batch = new() {
            SectionIds = [1, 2, 3],
            Names = ["pawns.work", "core.tick", "render.draw"],
            Subsystems = ["pawns", null, "render"],
        };

        aggregator.OnSectionRegistrations(batch);

        SectionStats? s1 = aggregator.FindSection(1);
        s1.Should().NotBeNull();
        s1!.Subsystem.Should().Be("pawns");

        SectionStats? s2 = aggregator.FindSection(2);
        s2.Should().NotBeNull();
        s2!.Subsystem.Should().BeNull();

        SectionStats? s3 = aggregator.FindSection(3);
        s3.Should().NotBeNull();
        s3!.Subsystem.Should().Be("render");
    }

    [Fact]
    public void OnSectionRegistrations_subsystem_defaults_to_null_when_subsystems_shorter_than_names() {
        SessionAggregator aggregator = new();
        SectionRegistrationsBatch batch = new() {
            SectionIds = [10, 11],
            Names = ["a", "b"],
            Subsystems = [],
        };

        aggregator.OnSectionRegistrations(batch);

        aggregator.FindSection(10)!.Subsystem.Should().BeNull();
        aggregator.FindSection(11)!.Subsystem.Should().BeNull();
    }

    [Fact]
    public void OnSectionBatch_counts_a_nested_child_on_the_same_lane_once() {
        SessionAggregator agg = new();

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20],
            ParentIds = [-1, 10],
            NodeIds = [1, 2],
            ParentNodeIds = [-1, 1],
            StartTimestamps = [100L, 150L],
            ElapsedTicks = [500L, 200L],
            FrameOrdinals = [1, 1],
            ThreadIds = [1, 1],
        });

        agg.Threads.Snapshot().Single(t => t.Id == 1).BusyTicks.Should().Be(500L);
    }

    [Fact]
    public void OnSectionBatch_skips_a_child_whose_parent_arrived_in_an_earlier_batch() {
        SessionAggregator agg = new();

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [10],
            ParentIds = [-1],
            NodeIds = [1],
            ParentNodeIds = [-1],
            StartTimestamps = [100L],
            ElapsedTicks = [500L],
            FrameOrdinals = [1],
            ThreadIds = [1],
        });

        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [20],
            ParentIds = [10],
            NodeIds = [2],
            ParentNodeIds = [1],
            StartTimestamps = [150L],
            ElapsedTicks = [200L],
            FrameOrdinals = [1],
            ThreadIds = [1],
        });

        agg.Threads.Snapshot().Single(t => t.Id == 1).BusyTicks.Should().Be(500L);
    }
}
