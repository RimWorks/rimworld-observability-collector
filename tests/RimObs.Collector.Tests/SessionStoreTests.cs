using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SessionStoreTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SessionStoreTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "rimobs-store-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_tempDir, "session.db");
    }

    public void Dispose() {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir)) {
            try {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException) {
                // SQLite may briefly retain file handles via the connection pool on Windows.
            }
        }
    }

    [Fact]
    public void Open_creates_directory_and_initializes_schema() {
        using SessionStore store = SessionStore.Open(_dbPath);

        Directory.Exists(_tempDir).Should().BeTrue();
        File.Exists(_dbPath).Should().BeTrue();

        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        object? result = cmd.ExecuteScalar();
        Convert.ToInt32(result).Should().Be(SessionStore.SchemaVersion);
    }

    [Fact]
    public void Open_rejects_empty_path() {
        Action act = () => SessionStore.Open("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteSessionMeta_then_Read_returns_same_values() {
        SessionMeta meta = new() {
            SessionId = "abc-123",
            StartedUtcTicks = 638_500_000_000_000_000L,
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 42L,
            LibraryVersion = "0.1.0",
            GameVersion = "1.6.4633",
        };

        using (SessionStore store = SessionStore.Open(_dbPath)) {
            store.WriteSessionMeta(meta);
        }

        using SessionStore reopened = SessionStore.Open(_dbPath);
        SessionMeta? round = reopened.ReadSessionMeta("abc-123");
        round.Should().NotBeNull();
        round!.SessionId.Should().Be(meta.SessionId);
        round.StartedUtcTicks.Should().Be(meta.StartedUtcTicks);
        round.StopwatchFrequency.Should().Be(meta.StopwatchFrequency);
        round.AnchorTimestamp.Should().Be(meta.AnchorTimestamp);
        round.LibraryVersion.Should().Be(meta.LibraryVersion);
        round.GameVersion.Should().Be(meta.GameVersion);
    }

    [Fact]
    public void Dispose_closes_connection_so_wal_sidecars_are_removed() {
        // regression: default pooling returned the sqlite handle to the pool instead of closing it, so
        // the -wal/-shm sidecars persisted. their removal is what this asserts, with teeth on Linux.
        SessionMeta meta = new() {
            SessionId = "wal-probe",
            StartedUtcTicks = 638_500_000_000_000_000L,
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 42L,
            LibraryVersion = "0.1.0",
            GameVersion = "1.6.4633",
        };

        using (SessionStore store = SessionStore.Open(_dbPath)) {
            store.WriteSessionMeta(meta);
        }

        File.Exists(_dbPath + "-wal").Should().BeFalse(
            "disposing the store must close the SQLite connection so the WAL is checkpointed "
            + "and the sidecar removed; a lingering pooled handle keeps session.db locked on Windows");
        File.Exists(_dbPath + "-shm").Should().BeFalse(
            "the shared-memory sidecar must also be cleaned up when the last connection closes");
    }

    [Fact]
    public void WriteSessionMeta_is_idempotent_on_same_session_id() {
        SessionMeta first = new() { SessionId = "s1", StartedUtcTicks = 1, StopwatchFrequency = 2, AnchorTimestamp = 3, LibraryVersion = "old", GameVersion = "1.6" };
        SessionMeta second = new() { SessionId = "s1", StartedUtcTicks = 10, StopwatchFrequency = 20, AnchorTimestamp = 30, LibraryVersion = "new", GameVersion = "1.6" };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteSessionMeta(first);
        store.WriteSessionMeta(second);

        SessionMeta? round = store.ReadSessionMeta("s1");
        round.Should().NotBeNull();
        round!.LibraryVersion.Should().Be("new");
        round.StartedUtcTicks.Should().Be(10);
    }

    [Fact]
    public void ReadSessionMeta_returns_null_for_unknown_session() {
        using SessionStore store = SessionStore.Open(_dbPath);
        store.ReadSessionMeta("nope").Should().BeNull();
    }

    [Fact]
    public void Open_with_mismatched_schema_drops_and_recreates_tables() {
        Directory.CreateDirectory(_tempDir);
        using (SqliteConnection setup = new($"Data Source={_dbPath}")) {
            setup.Open();
            using SqliteCommand create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE stale_table (id INTEGER); PRAGMA user_version = 0;";
            create.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using SessionStore store = SessionStore.Open(_dbPath);
        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='stale_table';";
        object? result = cmd.ExecuteScalar();
        result.Should().BeNull("stale table should have been dropped on schema mismatch");

        using SqliteCommand versionCmd = probe.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        Convert.ToInt32(versionCmd.ExecuteScalar()).Should().Be(SessionStore.SchemaVersion);
    }

    [Fact]
    public void Open_uses_WAL_journal_mode() {
        using SessionStore store = SessionStore.Open(_dbPath);

        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        string mode = ((string?)cmd.ExecuteScalar() ?? string.Empty).ToLowerInvariant();
        mode.Should().Be("wal");
    }

    [Fact]
    public void Disposed_store_rejects_writes() {
        SessionStore store = SessionStore.Open(_dbPath);
        store.Dispose();

        Action act = () => store.WriteSessionMeta(new SessionMeta { SessionId = "x" });
        act.Should().Throw<ObjectDisposedException>();
    }


    [Fact]
    public void WriteSectionsSnapshot_round_trips_per_section_stats() {
        SectionStats a = new() {
            SectionId = 1,
            Name = "core.tick",
            SampleCount = 42,
            TotalElapsedTicks = 12345,
            MinElapsedTicks = 100,
            MaxElapsedTicks = 9999,
            LastStartTimestamp = 77L,
        };
        SectionStats b = new() {
            SectionId = 2,
            Name = "core.draw",
            SampleCount = 7,
            TotalElapsedTicks = 700,
            MinElapsedTicks = long.MaxValue, // unset min sentinel
            MaxElapsedTicks = 200,
            LastStartTimestamp = 88L,
        };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteSectionsSnapshot([a, b]);

        store.CountSections().Should().Be(2);

        // Sentinel min should be stored as 0, not long.MaxValue, to keep dashboard math simple.
        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT min_elapsed_ticks FROM sections WHERE section_id = 2;";
        Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(0);
    }

    [Fact]
    public void WriteSectionsSnapshot_is_idempotent_for_same_section_id() {
        SectionStats stats = new() { SectionId = 1, Name = "s", SampleCount = 1, TotalElapsedTicks = 100, MinElapsedTicks = 10, MaxElapsedTicks = 100, LastStartTimestamp = 1 };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteSectionsSnapshot([stats]);
        stats.SampleCount = 5;
        stats.TotalElapsedTicks = 555;
        store.WriteSectionsSnapshot([stats]);

        store.CountSections().Should().Be(1);
        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT sample_count, total_elapsed_ticks FROM sections WHERE section_id = 1;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(5);
        reader.GetInt64(1).Should().Be(555);
    }

    [Fact]
    public void WriteMetricsSnapshot_round_trips_metric_and_per_label_rows() {
        MetricStats m = new(metricId: 10) { Name = "my.mod.frames", Kind = MetricKind.Counter, Unit = "count" };
        m.Labels["scene=map"] = new MetricLabelStats("scene=map") { LatestValue = 99, TotalSampleCount = 4 };
        m.Labels["scene=ui"] = new MetricLabelStats("scene=ui") { LatestValue = 17, TotalSampleCount = 1 };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteMetricsSnapshot([m]);

        store.CountMetrics().Should().Be(1);
        store.CountMetricLabels().Should().Be(2);

        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT canonical, latest_value, total_sample_count FROM metric_labels WHERE metric_id = 10 ORDER BY canonical;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be("scene=map");
        reader.GetInt64(1).Should().Be(99);
        reader.GetInt64(2).Should().Be(4);
        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be("scene=ui");
        reader.GetInt64(1).Should().Be(17);
        reader.GetInt64(2).Should().Be(1);
    }

    [Fact]
    public void WriteMetricsSnapshot_is_idempotent_and_updates_latest_value() {
        MetricStats m = new(metricId: 5) { Name = "g", Kind = MetricKind.Gauge, Unit = "b" };
        m.Labels[""] = new MetricLabelStats("") { LatestValue = 10, TotalSampleCount = 1 };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteMetricsSnapshot([m]);
        m.Labels[""].LatestValue = 25;
        m.Labels[""].TotalSampleCount = 3;
        store.WriteMetricsSnapshot([m]);

        store.CountMetricLabels().Should().Be(1);
        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT latest_value, total_sample_count FROM metric_labels WHERE metric_id = 5;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(25);
        reader.GetInt64(1).Should().Be(3);
    }

    [Fact]
    public void ReplaceGcEventsSnapshot_truncates_then_inserts_in_order() {
        GcEventRecord a = new(generation: 0, pauseType: GcPauseType.Background, heapBefore: 1000, heapAfter: 800, durationMicros: 50, ticks: 100, allocationRateBytesPerMinute: 5000);
        GcEventRecord b = new(generation: 2, pauseType: GcPauseType.Foreground, heapBefore: 5000, heapAfter: 4500, durationMicros: 200, ticks: 200, allocationRateBytesPerMinute: 7500);

        using SessionStore store = SessionStore.Open(_dbPath);
        store.ReplaceGcEventsSnapshot([a, b]);
        store.CountGcEvents().Should().Be(2);

        store.ReplaceGcEventsSnapshot([b]);
        store.CountGcEvents().Should().Be(1);

        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT generation, pause_type, heap_before, heap_after, duration_micros, ticks, allocation_rate_bpm FROM gc_events;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(2);
        reader.GetInt32(1).Should().Be(0);
        reader.GetInt64(2).Should().Be(5000);
        reader.GetInt64(3).Should().Be(4500);
        reader.GetInt64(4).Should().Be(200);
        reader.GetInt64(5).Should().Be(200);
        reader.GetInt64(6).Should().Be(7500);
    }

    [Fact]
    public void ReplaceGcEventsSnapshot_with_empty_array_clears_table() {
        using SessionStore store = SessionStore.Open(_dbPath);
        store.ReplaceGcEventsSnapshot([new GcEventRecord(0, GcPauseType.Foreground, 0, 0, 0, 0, 0)]);
        store.CountGcEvents().Should().Be(1);
        store.ReplaceGcEventsSnapshot([]);
        store.CountGcEvents().Should().Be(0);
    }


    [Fact]
    public void WriteCallTreeSnapshot_round_trips_edges_keyed_by_parent_and_section() {
        CallEdgeStats root = new() { ParentId = -1, SectionId = 1, CallCount = 3, TotalElapsedTicks = 300 };
        CallEdgeStats child = new() { ParentId = 1, SectionId = 2, CallCount = 5, TotalElapsedTicks = 120 };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteCallTreeSnapshot([root, child]);

        store.CountCallTreeEdges().Should().Be(2);

        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT call_count, total_elapsed_ticks FROM call_tree_edges WHERE parent_id = 1 AND section_id = 2;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(5);
        reader.GetInt64(1).Should().Be(120);
    }

    [Fact]
    public void WriteCallTreeSnapshot_is_idempotent_on_same_edge_key() {
        CallEdgeStats edge = new() { ParentId = 1, SectionId = 2, CallCount = 5, TotalElapsedTicks = 120 };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteCallTreeSnapshot([edge]);

        edge.CallCount = 9;
        edge.TotalElapsedTicks = 400;
        store.WriteCallTreeSnapshot([edge]);

        store.CountCallTreeEdges().Should().Be(1);

        using SqliteConnection probe = new($"Data Source={_dbPath}");
        probe.Open();
        using SqliteCommand cmd = probe.CreateCommand();
        cmd.CommandText = "SELECT call_count FROM call_tree_edges WHERE parent_id = 1 AND section_id = 2;";
        Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(9);
    }
}

public sealed class SessionStoreSubsystemTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SessionStoreSubsystemTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "rimobs-subsys-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_tempDir, "session.db");
    }

    public void Dispose() {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir)) {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void WriteSectionsSnapshot_round_trips_subsystem_string_and_null() {
        SectionStats withSub = new() {
            SectionId = 1,
            Name = "pawns.work",
            Subsystem = "pawns",
            SampleCount = 10,
            TotalElapsedTicks = 1000,
            MinElapsedTicks = 50,
            MaxElapsedTicks = 200,
            LastStartTimestamp = 1L,
        };
        SectionStats noSub = new() {
            SectionId = 2,
            Name = "core.tick",
            Subsystem = null,
            SampleCount = 5,
            TotalElapsedTicks = 500,
            MinElapsedTicks = 40,
            MaxElapsedTicks = 150,
            LastStartTimestamp = 2L,
        };

        using SessionStore store = SessionStore.Open(_dbPath);
        store.WriteSectionsSnapshot([withSub, noSub]);

        List<SectionRow> rows = store.GetSectionMetadata();
        rows.Should().HaveCount(2);

        SectionRow? pawns = rows.FirstOrDefault(r => r.SectionId == 1);
        pawns.Should().NotBeNull();
        pawns!.Name.Should().Be("pawns.work");
        pawns.Subsystem.Should().Be("pawns");

        SectionRow? core = rows.FirstOrDefault(r => r.SectionId == 2);
        core.Should().NotBeNull();
        core!.Name.Should().Be("core.tick");
        core.Subsystem.Should().BeNull();
    }

    [Theory]
    [InlineData("sections", "\"sections\"")]
    [InlineData("odd name", "\"odd name\"")]
    [InlineData("we\"ird", "\"we\"\"ird\"")]
    [InlineData("a\"; DROP TABLE metrics; --", "\"a\"\"; DROP TABLE metrics; --\"")]
    public void QuoteIdentifier_doubles_embedded_quotes(string name, string expected) {
        SessionStore.QuoteIdentifier(name).Should().Be(expected);
    }
}
