using RimWorks.RimObs.Wire.Control;
using Microsoft.Data.Sqlite;

namespace RimWorks.RimObs.Collector.Storage;

public sealed class DynamicPatchStore : IDisposable {
    private readonly SqliteConnection _conn;

    private DynamicPatchStore(SqliteConnection conn) {
        _conn = conn;
        EnsureSchema();
    }

    public static DynamicPatchStore OpenInMemory() {
        SqliteConnection conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return new DynamicPatchStore(conn);
    }

    public static DynamicPatchStore Open(string path) {
        SqliteConnection conn = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Pooling=False");
        conn.Open();
        ConfigureConcurrency(conn);
        return new DynamicPatchStore(conn);
    }

    private static void ConfigureConcurrency(SqliteConnection conn) {
        using SqliteCommand timeout = conn.CreateCommand();
        timeout.CommandText = "PRAGMA busy_timeout=5000;";
        timeout.ExecuteNonQuery();

        using SqliteCommand wal = conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();
    }

    private void EnsureSchema() {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dynamic_patches (
              id INTEGER PRIMARY KEY,
              type_full_name TEXT NOT NULL,
              method_name TEXT NOT NULL,
              param_types_joined TEXT NOT NULL,
              created_utc TEXT NOT NULL,
              last_status TEXT NOT NULL,
              last_error TEXT,
              UNIQUE(type_full_name, method_name, param_types_joined)
            );
            """;
        cmd.ExecuteNonQuery();
        AddLivePatchIdColumn();
    }

    // stores written before the live-id fix have no such column, and sqlite has no
    // ADD COLUMN IF NOT EXISTS.
    private void AddLivePatchIdColumn() {
        using SqliteCommand check = _conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('dynamic_patches') WHERE name='live_patch_id';";
        if ((long)check.ExecuteScalar()! > 0)
            return;

        using SqliteCommand add = _conn.CreateCommand();
        add.CommandText = "ALTER TABLE dynamic_patches ADD COLUMN live_patch_id INTEGER;";
        add.ExecuteNonQuery();
    }

    public long Insert(string typeFullName, string methodName, string paramTypesJoined) {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dynamic_patches (type_full_name, method_name, param_types_joined, created_utc, last_status)
            VALUES ($t, $m, $p, $c, 'pending')
            ON CONFLICT(type_full_name, method_name, param_types_joined) DO NOTHING;
            SELECT id FROM dynamic_patches WHERE type_full_name=$t AND method_name=$m AND param_types_joined=$p;
            """;
        cmd.Parameters.AddWithValue("$t", typeFullName);
        cmd.Parameters.AddWithValue("$m", methodName);
        cmd.Parameters.AddWithValue("$p", paramTypesJoined);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<DynamicPatchRow> List() {
        List<DynamicPatchRow> rows = new();
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, type_full_name, method_name, param_types_joined, created_utc, last_status, last_error, live_patch_id FROM dynamic_patches ORDER BY id;";
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read()) {
            rows.Add(new DynamicPatchRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), ParseStatus(r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetInt32(7)));
        }
        return rows;
    }

    public DynamicPatchRow? Find(long id) {
        foreach (DynamicPatchRow row in List()) {
            if (row.Id == id)
                return row;
        }
        return null;
    }

    /// <summary>Records the id the library handed back, so an unpatch can target the right one.</summary>
    public void UpdateLivePatchId(long id, int? livePatchId) {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE dynamic_patches SET live_patch_id=$live WHERE id=$id";
        cmd.Parameters.AddWithValue("$live", (object?)livePatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears every live id, because a new game session renumbers them all from 1.</summary>
    public void ClearLivePatchIds() {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE dynamic_patches SET live_patch_id=NULL;";
        cmd.ExecuteNonQuery();
    }

    public void UpdateStatus(long id, PatchStatus status, string? error) {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE dynamic_patches SET last_status=$s, last_error=$e WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", ToText(status));
        cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static string ToText(PatchStatus status) => status.ToString().ToLowerInvariant();

    private static PatchStatus ParseStatus(string raw) =>
        Enum.TryParse(raw, true, out PatchStatus status) ? status : PatchStatus.Pending;

    public bool Delete(long id) {
        using SqliteCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dynamic_patches WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void Dispose() {
        _conn.Dispose();
    }
}
