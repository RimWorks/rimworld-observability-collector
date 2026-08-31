using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SqliteSessionPersisterTests : IDisposable {
    private readonly string _tempDir;

    public SqliteSessionPersisterTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "rimobs-persister-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir)) {
            try {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException) {
                // SQLite may retain file handles briefly on Windows.
            }
        }
    }

    [Fact]
    public void Ctor_creates_directory() {
        using SqliteSessionPersister persister = new(_tempDir);
        Directory.Exists(_tempDir).Should().BeTrue();
        persister.SessionsDirectory.Should().Be(_tempDir);
    }

    [Fact]
    public void Ctor_rejects_empty_dir() {
        Action act = () => _ = new SqliteSessionPersister("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteSessionMeta_creates_per_session_db_file() {
        using SqliteSessionPersister persister = new(_tempDir);
        SessionMeta meta = new() {
            SessionId = "alpha",
            StartedUtcTicks = 100L,
            StopwatchFrequency = 10_000L,
            AnchorTimestamp = 1L,
            LibraryVersion = "0.1",
            GameVersion = "1.6",
        };

        persister.WriteSessionMeta(meta);

        string expectedPath = Path.Combine(_tempDir, "alpha.db");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public void WriteSessionMeta_for_different_sessions_creates_separate_db_files() {
        using SqliteSessionPersister persister = new(_tempDir);
        persister.WriteSessionMeta(new SessionMeta { SessionId = "one" });
        persister.WriteSessionMeta(new SessionMeta { SessionId = "two" });

        File.Exists(Path.Combine(_tempDir, "one.db")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "two.db")).Should().BeTrue();
    }

    [Fact]
    public void WriteSessionMeta_for_same_session_is_idempotent() {
        using SqliteSessionPersister persister = new(_tempDir);
        persister.WriteSessionMeta(new SessionMeta { SessionId = "s", LibraryVersion = "old" });
        persister.WriteSessionMeta(new SessionMeta { SessionId = "s", LibraryVersion = "new" });

        using SessionStore probe = SessionStore.Open(Path.Combine(_tempDir, "s.db"));
        SessionMeta? read = probe.ReadSessionMeta("s");
        read.Should().NotBeNull();
        read!.LibraryVersion.Should().Be("new");
    }

    [Fact]
    public void WriteSessionMeta_sanitizes_invalid_characters_in_session_id() {
        using SqliteSessionPersister persister = new(_tempDir);
        // Build an id that includes every char the platform considers invalid in a filename.
        char[] invalid = Path.GetInvalidFileNameChars();
        string trouble = "weird" + new string(invalid) + "id";
        persister.WriteSessionMeta(new SessionMeta { SessionId = trouble });

        string[] files = Directory.GetFiles(_tempDir, "*.db");
        files.Should().ContainSingle();
        string fileName = Path.GetFileName(files[0]);
        foreach (char ch in invalid)
            fileName.Should().NotContain(ch.ToString(), because: $"invalid char {(int)ch} must be replaced");
    }

    [Fact]
    public void WriteSessionMeta_rejects_empty_session_id() {
        using SqliteSessionPersister persister = new(_tempDir);
        Action act = () => persister.WriteSessionMeta(new SessionMeta { SessionId = "" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Disposed_persister_rejects_writes() {
        SqliteSessionPersister persister = new(_tempDir);
        persister.Dispose();
        Action act = () => persister.WriteSessionMeta(new SessionMeta { SessionId = "x" });
        act.Should().Throw<ObjectDisposedException>();
    }
}
