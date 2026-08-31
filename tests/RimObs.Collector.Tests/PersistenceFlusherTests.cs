using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class PersistenceFlusherTests : IDisposable {
    private readonly string _tempDir;

    public PersistenceFlusherTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "rimobs-flusher-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir)) {
            try {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException) {
                // SQLite may briefly retain file handles on Windows.
            }
        }
    }

    [Fact]
    public void Ctor_rejects_zero_or_negative_interval() {
        SessionAggregator agg = new();
        using SpyPersister persister = new();
        Action zero = () => _ = new PersistenceFlusher(agg, persister, TimeSpan.Zero);
        Action neg = () => _ = new PersistenceFlusher(agg, persister, TimeSpan.FromMilliseconds(-1));
        zero.Should().Throw<ArgumentOutOfRangeException>();
        neg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FlushOnce_noops_when_session_has_not_started() {
        SessionAggregator agg = new();
        using SpyPersister persister = new();
        PersistenceFlusher flusher = new(agg, persister, TimeSpan.FromSeconds(1));

        flusher.FlushOnce();

        persister.WrittenSections.Should().BeEmpty();
        persister.WrittenMetrics.Should().BeEmpty();
        persister.WrittenGc.Should().BeEmpty();
    }

    [Fact]
    public void FlushOnce_writes_sections_metrics_gc_when_session_present() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = "abc", StartedUtcTicks = 1, StopwatchFrequency = 1, AnchorTimestamp = 0 });
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [1],
            Names = ["core.tick"],
        });
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [1],
            ElapsedTicks = [1000],
            StartTimestamps = [42],
        });
        agg.OnGcEvents(new GcEventsBatch {
            Generations = [0],
            PauseTypes = [1],
            HeapBefore = [100],
            HeapAfter = [80],
            DurationMicros = [5],
            Ticks = [1],
            AllocationRateBytesPerMinute = [0],
        });

        using SpyPersister persister = new();
        PersistenceFlusher flusher = new(agg, persister, TimeSpan.FromSeconds(1));

        flusher.FlushOnce();

        persister.WrittenSections.Should().HaveCount(1);
        persister.WrittenSections[0].sessionId.Should().Be("abc");
        persister.WrittenSections[0].sections.Should().HaveCount(1);
        persister.WrittenGc.Should().HaveCount(1);
        persister.WrittenGc[0].sessionId.Should().Be("abc");
        persister.WrittenGc[0].events.Should().HaveCount(1);
    }

    [Fact]
    public void FlushOnce_swallows_persister_exceptions_via_ExecuteAsync_loop() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = "x", StartedUtcTicks = 1, StopwatchFrequency = 1, AnchorTimestamp = 0 });
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [1],
            Names = ["n"],
        });
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [1],
            ElapsedTicks = [1],
            StartTimestamps = [1],
        });

        using ThrowingPersister persister = new();
        PersistenceFlusher flusher = new(agg, persister, TimeSpan.FromSeconds(1));

        // FlushOnce itself throws; the BackgroundService loop swallows it.
        Action act = () => flusher.FlushOnce();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void End_to_end_flush_writes_rows_to_per_session_sqlite_db() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = "live", StartedUtcTicks = 100, StopwatchFrequency = 10_000_000, AnchorTimestamp = 0 });
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [1],
            Names = ["core.tick"],
        });
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [1],
            ElapsedTicks = [500],
            StartTimestamps = [11],
        });

        using SqliteSessionPersister persister = new(_tempDir);
        // Seed session_meta so subsequent snapshots have a row to attach to.
        persister.WriteSessionMeta(agg.Meta!);

        PersistenceFlusher flusher = new(agg, persister, TimeSpan.FromSeconds(1));
        flusher.FlushOnce();

        string dbPath = Path.Combine(_tempDir, "live.db");
        File.Exists(dbPath).Should().BeTrue();

        using SqliteConnection probe = new($"Data Source={dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sections;";
        Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(1);
    }

    private sealed class SpyPersister : ISessionPersister {
        public List<SessionMeta> WrittenMetas { get; } = [];
        public List<(string sessionId, IReadOnlyCollection<SectionStats> sections)> WrittenSections { get; } = [];
        public List<(string sessionId, IReadOnlyCollection<MetricStats> metrics)> WrittenMetrics { get; } = [];
        public List<(string sessionId, GcEventRecord[] events)> WrittenGc { get; } = [];
        public List<(string sessionId, IReadOnlyCollection<CallEdgeStats> edges)> WrittenCallTree { get; } = [];

        public void WriteSessionMeta(SessionMeta meta) => WrittenMetas.Add(meta);
        public void WriteSectionsSnapshot(string sessionId, IReadOnlyCollection<SectionStats> sections) => WrittenSections.Add((sessionId, sections));
        public void WriteMetricsSnapshot(string sessionId, IReadOnlyCollection<MetricStats> metrics) => WrittenMetrics.Add((sessionId, metrics));
        public void ReplaceGcEventsSnapshot(string sessionId, GcEventRecord[] events) => WrittenGc.Add((sessionId, events));
        public void WriteCallTreeSnapshot(string sessionId, IReadOnlyCollection<CallEdgeStats> edges) => WrittenCallTree.Add((sessionId, edges));
        public void Dispose() { }
    }

    private sealed class ThrowingPersister : ISessionPersister {
        public void WriteSessionMeta(SessionMeta meta) { }
        public void WriteSectionsSnapshot(string sessionId, IReadOnlyCollection<SectionStats> sections) => throw new InvalidOperationException("disk full");
        public void WriteMetricsSnapshot(string sessionId, IReadOnlyCollection<MetricStats> metrics) { }
        public void ReplaceGcEventsSnapshot(string sessionId, GcEventRecord[] events) { }
        public void WriteCallTreeSnapshot(string sessionId, IReadOnlyCollection<CallEdgeStats> edges) { }
        public void Dispose() { }
    }
}
