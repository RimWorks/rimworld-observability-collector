using System.Globalization;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using Microsoft.Data.Sqlite;

namespace RimWorks.RimObs.Collector.Storage;

public sealed class SessionStore : IDisposable {
    public const int SchemaVersion = 4;
    private const string SchemaVersionPragma = "user_version";

    private readonly SqliteConnection _connection;
    private bool _disposed;

    private SessionStore(SqliteConnection connection) {
        _connection = connection;
    }

    public string DatabasePath => _connection.DataSource;

    public static SessionStore Open(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("dbPath must be a non-empty path", nameof(dbPath));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        SqliteConnectionStringBuilder csb = new() {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        };
        SqliteConnection connection = new(csb.ConnectionString);
        connection.Open();

        SetWalMode(connection);

        int existing = ReadSchemaVersion(connection);
        if (existing != SchemaVersion) {
            DropAllTables(connection);
            CreateSchema(connection);
            WriteSchemaVersion(connection, SchemaVersion);
        }

        return new SessionStore(connection);
    }

    public static SessionStore OpenReadOnly(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("dbPath must be a non-empty path", nameof(dbPath));

        SqliteConnectionStringBuilder csb = new() {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        };
        SqliteConnection connection = new(csb.ConnectionString);
        connection.Open();
        return new SessionStore(connection);
    }

    public void WriteSessionMeta(SessionMeta meta) {
        ArgumentNullException.ThrowIfNull(meta);
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO session_meta (session_id, started_utc_ticks, stopwatch_frequency, anchor_timestamp, library_version, game_version)
VALUES ($id, $started, $freq, $anchor, $lib, $game)
ON CONFLICT(session_id) DO UPDATE SET
    started_utc_ticks = excluded.started_utc_ticks,
    stopwatch_frequency = excluded.stopwatch_frequency,
    anchor_timestamp = excluded.anchor_timestamp,
    library_version = excluded.library_version,
    game_version = excluded.game_version;
";
        cmd.Parameters.AddWithValue("$id", meta.SessionId);
        cmd.Parameters.AddWithValue("$started", meta.StartedUtcTicks);
        cmd.Parameters.AddWithValue("$freq", meta.StopwatchFrequency);
        cmd.Parameters.AddWithValue("$anchor", meta.AnchorTimestamp);
        cmd.Parameters.AddWithValue("$lib", meta.LibraryVersion ?? string.Empty);
        cmd.Parameters.AddWithValue("$game", meta.GameVersion ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    public SessionMeta? ReadSessionMeta(string sessionId) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId must be non-empty", nameof(sessionId));
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"
SELECT session_id, started_utc_ticks, stopwatch_frequency, anchor_timestamp, library_version, game_version
FROM session_meta WHERE session_id = $id;
";
        cmd.Parameters.AddWithValue("$id", sessionId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new SessionMeta {
            SessionId = reader.GetString(0),
            StartedUtcTicks = reader.GetInt64(1),
            StopwatchFrequency = reader.GetInt64(2),
            AnchorTimestamp = reader.GetInt64(3),
            LibraryVersion = reader.GetString(4),
            GameVersion = reader.GetString(5),
        };
    }

    public SessionMeta? ReadFirstSessionMeta() {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"
SELECT session_id, started_utc_ticks, stopwatch_frequency, anchor_timestamp, library_version, game_version
FROM session_meta LIMIT 1;
";

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new SessionMeta {
            SessionId = reader.GetString(0),
            StartedUtcTicks = reader.GetInt64(1),
            StopwatchFrequency = reader.GetInt64(2),
            AnchorTimestamp = reader.GetInt64(3),
            LibraryVersion = reader.GetString(4),
            GameVersion = reader.GetString(5),
        };
    }


    public void WriteSectionsSnapshot(IReadOnlyCollection<SectionStats> sections) {
        ArgumentNullException.ThrowIfNull(sections);
        ThrowIfDisposed();

        using SqliteTransaction tx = _connection.BeginTransaction();
        using SqliteCommand upsert = _connection.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"
INSERT INTO sections (section_id, name, subsystem, sample_count, total_elapsed_ticks, min_elapsed_ticks, max_elapsed_ticks, last_start_timestamp)
VALUES ($id, $name, $subsystem, $samples, $total, $min, $max, $lastStart)
ON CONFLICT(section_id) DO UPDATE SET
    name = excluded.name,
    subsystem = excluded.subsystem,
    sample_count = excluded.sample_count,
    total_elapsed_ticks = excluded.total_elapsed_ticks,
    min_elapsed_ticks = excluded.min_elapsed_ticks,
    max_elapsed_ticks = excluded.max_elapsed_ticks,
    last_start_timestamp = excluded.last_start_timestamp;
";
        SqliteParameter pId = upsert.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter pName = upsert.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter pSubsystem = upsert.Parameters.Add("$subsystem", SqliteType.Text);
        SqliteParameter pSamples = upsert.Parameters.Add("$samples", SqliteType.Integer);
        SqliteParameter pTotal = upsert.Parameters.Add("$total", SqliteType.Integer);
        SqliteParameter pMin = upsert.Parameters.Add("$min", SqliteType.Integer);
        SqliteParameter pMax = upsert.Parameters.Add("$max", SqliteType.Integer);
        SqliteParameter pLast = upsert.Parameters.Add("$lastStart", SqliteType.Integer);

        foreach (SectionStats stats in sections) {
            pId.Value = stats.SectionId;
            pName.Value = stats.Name ?? string.Empty;
            pSubsystem.Value = stats.Subsystem is null ? (object)DBNull.Value : stats.Subsystem;
            pSamples.Value = Interlocked.Read(ref stats.SampleCount);
            pTotal.Value = Interlocked.Read(ref stats.TotalElapsedTicks);
            long min = Interlocked.Read(ref stats.MinElapsedTicks);
            pMin.Value = min == long.MaxValue ? 0L : min;
            pMax.Value = Interlocked.Read(ref stats.MaxElapsedTicks);
            pLast.Value = stats.LastStartTimestamp;
            upsert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void WriteMetricsSnapshot(IReadOnlyCollection<MetricStats> metrics) {
        ArgumentNullException.ThrowIfNull(metrics);
        ThrowIfDisposed();

        using SqliteTransaction tx = _connection.BeginTransaction();

        using SqliteCommand upsertMetric = _connection.CreateCommand();
        upsertMetric.Transaction = tx;
        upsertMetric.CommandText = @"
INSERT INTO metrics (metric_id, name, kind, unit)
VALUES ($id, $name, $kind, $unit)
ON CONFLICT(metric_id) DO UPDATE SET
    name = excluded.name,
    kind = excluded.kind,
    unit = excluded.unit;
";
        SqliteParameter pId = upsertMetric.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter pName = upsertMetric.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter pKind = upsertMetric.Parameters.Add("$kind", SqliteType.Integer);
        SqliteParameter pUnit = upsertMetric.Parameters.Add("$unit", SqliteType.Text);

        using SqliteCommand upsertLabel = _connection.CreateCommand();
        upsertLabel.Transaction = tx;
        upsertLabel.CommandText = @"
INSERT INTO metric_labels (metric_id, canonical, latest_value, total_sample_count)
VALUES ($mid, $canon, $latest, $samples)
ON CONFLICT(metric_id, canonical) DO UPDATE SET
    latest_value = excluded.latest_value,
    total_sample_count = excluded.total_sample_count;
";
        SqliteParameter lMid = upsertLabel.Parameters.Add("$mid", SqliteType.Integer);
        SqliteParameter lCanon = upsertLabel.Parameters.Add("$canon", SqliteType.Text);
        SqliteParameter lLatest = upsertLabel.Parameters.Add("$latest", SqliteType.Integer);
        SqliteParameter lSamples = upsertLabel.Parameters.Add("$samples", SqliteType.Integer);

        foreach (MetricStats metric in metrics) {
            pId.Value = metric.MetricId;
            pName.Value = metric.Name ?? string.Empty;
            pKind.Value = (byte)metric.Kind;
            pUnit.Value = metric.Unit ?? string.Empty;
            upsertMetric.ExecuteNonQuery();

            foreach (MetricLabelStats label in metric.Labels.Values) {
                lMid.Value = metric.MetricId;
                lCanon.Value = label.Canonical ?? string.Empty;
                lLatest.Value = Interlocked.Read(ref label.LatestValue);
                lSamples.Value = Interlocked.Read(ref label.TotalSampleCount);
                upsertLabel.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void ReplaceGcEventsSnapshot(GcEventRecord[] events) {
        ArgumentNullException.ThrowIfNull(events);
        ThrowIfDisposed();

        using SqliteTransaction tx = _connection.BeginTransaction();

        using (SqliteCommand truncate = _connection.CreateCommand()) {
            truncate.Transaction = tx;
            truncate.CommandText = "DELETE FROM gc_events;";
            truncate.ExecuteNonQuery();
        }

        if (events.Length > 0) {
            using SqliteCommand insert = _connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"
INSERT INTO gc_events (generation, pause_type, heap_before, heap_after, duration_micros, ticks, allocation_rate_bpm)
VALUES ($gen, $pause, $hb, $ha, $dur, $ticks, $rate);
";
            SqliteParameter pGen = insert.Parameters.Add("$gen", SqliteType.Integer);
            SqliteParameter pPause = insert.Parameters.Add("$pause", SqliteType.Integer);
            SqliteParameter pHb = insert.Parameters.Add("$hb", SqliteType.Integer);
            SqliteParameter pHa = insert.Parameters.Add("$ha", SqliteType.Integer);
            SqliteParameter pDur = insert.Parameters.Add("$dur", SqliteType.Integer);
            SqliteParameter pTicks = insert.Parameters.Add("$ticks", SqliteType.Integer);
            SqliteParameter pRate = insert.Parameters.Add("$rate", SqliteType.Integer);

            foreach (GcEventRecord e in events) {
                pGen.Value = e.Generation;
                pPause.Value = (byte)e.PauseType;
                pHb.Value = e.HeapBefore;
                pHa.Value = e.HeapAfter;
                pDur.Value = e.DurationMicros;
                pTicks.Value = e.Ticks;
                pRate.Value = e.AllocationRateBytesPerMinute;
                insert.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void WriteCallTreeSnapshot(IReadOnlyCollection<CallEdgeStats> edges) {
        ArgumentNullException.ThrowIfNull(edges);
        ThrowIfDisposed();

        using SqliteTransaction tx = _connection.BeginTransaction();
        using SqliteCommand upsert = _connection.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = @"
INSERT INTO call_tree_edges (parent_id, section_id, call_count, total_elapsed_ticks)
VALUES ($parent, $section, $count, $total)
ON CONFLICT(parent_id, section_id) DO UPDATE SET
    call_count = excluded.call_count,
    total_elapsed_ticks = excluded.total_elapsed_ticks;
";
        SqliteParameter pParent = upsert.Parameters.Add("$parent", SqliteType.Integer);
        SqliteParameter pSection = upsert.Parameters.Add("$section", SqliteType.Integer);
        SqliteParameter pCount = upsert.Parameters.Add("$count", SqliteType.Integer);
        SqliteParameter pTotal = upsert.Parameters.Add("$total", SqliteType.Integer);

        foreach (CallEdgeStats edge in edges) {
            pParent.Value = edge.ParentId;
            pSection.Value = edge.SectionId;
            pCount.Value = Interlocked.Read(ref edge.CallCount);
            pTotal.Value = Interlocked.Read(ref edge.TotalElapsedTicks);
            upsert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public int CountCallTreeEdges() => CountRows("SELECT COUNT(*) FROM call_tree_edges;");

    public int CountSections() => CountRows("SELECT COUNT(*) FROM sections;");

    public int CountMetrics() => CountRows("SELECT COUNT(*) FROM metrics;");

    public int CountMetricLabels() => CountRows("SELECT COUNT(*) FROM metric_labels;");

    public int CountGcEvents() => CountRows("SELECT COUNT(*) FROM gc_events;");

    private int CountRows(string sql) {
        ThrowIfDisposed();
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public List<SectionRow> GetSectionMetadata() {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT section_id, name, subsystem FROM sections ORDER BY section_id;";

        List<SectionRow> rows = [];
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) {
            rows.Add(new SectionRow(
                SectionId: reader.GetInt32(0),
                Name: reader.GetString(1),
                Subsystem: reader.IsDBNull(2) ? null : reader.GetString(2)
            ));
        }
        return rows;
    }

    public List<SectionStatsRow> GetSectionStats() {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT section_id, name, sample_count, total_elapsed_ticks, min_elapsed_ticks, max_elapsed_ticks FROM sections ORDER BY section_id;";

        List<SectionStatsRow> rows = [];
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) {
            rows.Add(new SectionStatsRow(
                SectionId: reader.GetInt32(0),
                Name: reader.GetString(1),
                SampleCount: reader.GetInt64(2),
                TotalElapsedTicks: reader.GetInt64(3),
                MinElapsedTicks: reader.GetInt64(4),
                MaxElapsedTicks: reader.GetInt64(5)));
        }
        return rows;
    }

    public List<MetricRow> GetMetrics() {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT metric_id, name, kind, unit FROM metrics ORDER BY metric_id;";

        List<MetricRow> rows = [];
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) {
            rows.Add(new MetricRow(
                MetricId: reader.GetInt32(0),
                Name: reader.GetString(1),
                Kind: (byte)reader.GetInt32(2),
                Unit: reader.GetString(3)));
        }
        return rows;
    }

    public List<MetricLabelRow> GetMetricLabels() {
        ThrowIfDisposed();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT metric_id, canonical, latest_value, total_sample_count FROM metric_labels ORDER BY metric_id, canonical;";

        List<MetricLabelRow> rows = [];
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) {
            rows.Add(new MetricLabelRow(
                MetricId: reader.GetInt32(0),
                Canonical: reader.GetString(1),
                LatestValue: reader.GetInt64(2),
                TotalSampleCount: reader.GetInt64(3)));
        }
        return rows;
    }

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _connection.Dispose();
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void SetWalMode(SqliteConnection connection) {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
    }

    private static int ReadSchemaVersion(SqliteConnection connection) {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {SchemaVersionPragma};";
        object? result = cmd.ExecuteScalar();
        if (result is long l)
            return (int)l;
        if (result is int i)
            return i;
        return 0;
    }

    private static void WriteSchemaVersion(SqliteConnection connection, int version) {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA {SchemaVersionPragma} = {version};");
        cmd.ExecuteNonQuery();
    }

    private static void DropAllTables(SqliteConnection connection) {
        using SqliteCommand listCmd = connection.CreateCommand();
        listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        List<string> tables = [];
        using (SqliteDataReader reader = listCmd.ExecuteReader()) {
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        foreach (string table in tables) {
            using SqliteCommand dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS " + QuoteIdentifier(table) + ";";
            dropCmd.ExecuteNonQuery();
        }
    }

    // SQLite has no parameter slot for an identifier, so quote it the way SQLite does:
    // wrap in double quotes and double any quote already inside the name.
    internal static string QuoteIdentifier(string name) {
        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void CreateSchema(SqliteConnection connection) {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE session_meta (
    session_id TEXT PRIMARY KEY NOT NULL,
    started_utc_ticks INTEGER NOT NULL,
    stopwatch_frequency INTEGER NOT NULL,
    anchor_timestamp INTEGER NOT NULL,
    library_version TEXT NOT NULL,
    game_version TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE sections (
    section_id INTEGER PRIMARY KEY NOT NULL,
    name TEXT NOT NULL,
    subsystem TEXT NULL,
    sample_count INTEGER NOT NULL,
    total_elapsed_ticks INTEGER NOT NULL,
    min_elapsed_ticks INTEGER NOT NULL,
    max_elapsed_ticks INTEGER NOT NULL,
    last_start_timestamp INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE metrics (
    metric_id INTEGER PRIMARY KEY NOT NULL,
    name TEXT NOT NULL,
    kind INTEGER NOT NULL,
    unit TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE metric_labels (
    metric_id INTEGER NOT NULL,
    canonical TEXT NOT NULL,
    latest_value INTEGER NOT NULL,
    total_sample_count INTEGER NOT NULL,
    PRIMARY KEY (metric_id, canonical)
) WITHOUT ROWID;

CREATE TABLE gc_events (
    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    generation INTEGER NOT NULL,
    pause_type INTEGER NOT NULL,
    heap_before INTEGER NOT NULL,
    heap_after INTEGER NOT NULL,
    duration_micros INTEGER NOT NULL,
    ticks INTEGER NOT NULL,
    allocation_rate_bpm INTEGER NOT NULL
);

CREATE TABLE call_tree_edges (
    parent_id INTEGER NOT NULL,
    section_id INTEGER NOT NULL,
    call_count INTEGER NOT NULL,
    total_elapsed_ticks INTEGER NOT NULL,
    PRIMARY KEY (parent_id, section_id)
) WITHOUT ROWID;
";
        cmd.ExecuteNonQuery();
    }
}

public sealed record SectionRow(int SectionId, string Name, string? Subsystem);

public sealed record SectionStatsRow(
    int SectionId,
    string Name,
    long SampleCount,
    long TotalElapsedTicks,
    long MinElapsedTicks,
    long MaxElapsedTicks);

public sealed record MetricRow(int MetricId, string Name, byte Kind, string Unit);

public sealed record MetricLabelRow(int MetricId, string Canonical, long LatestValue, long TotalSampleCount);
