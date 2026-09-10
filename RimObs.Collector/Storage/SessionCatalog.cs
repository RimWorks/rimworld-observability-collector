using System.Collections.Generic;
using System.IO;
using RimWorks.RimObs.Wire;
using Microsoft.Data.Sqlite;

namespace RimWorks.RimObs.Collector.Storage;

/// <summary>
/// A stored session and the label the user gave it. The name is not on <see cref="SessionMeta"/>
/// because that is the wire contract the net472 library speaks, and the library never sends one.
/// </summary>
public sealed record StoredSession(SessionMeta Meta, string Name);

public static class SessionCatalog {
    public static IReadOnlyList<StoredSession> List(string sessionsDir) {
        List<StoredSession> result = new List<StoredSession>();
        if (string.IsNullOrWhiteSpace(sessionsDir) || !Directory.Exists(sessionsDir))
            return result;

        foreach (string file in Directory.EnumerateFiles(sessionsDir, "*.db")) {
            StoredSession? stored = TryRead(file);
            if (stored != null)
                result.Add(stored);
        }

        return result;
    }

    private static StoredSession? TryRead(string dbPath) {
        try {
            using SessionStore store = SessionStore.OpenReadOnly(dbPath);
            SessionMeta? meta = store.ReadFirstSessionMeta();
            return meta is null ? null : new StoredSession(meta, store.ReadSessionName());
        }
        catch (SqliteException) {
            return null;
        }
        catch (IOException) {
            return null;
        }
    }
}
