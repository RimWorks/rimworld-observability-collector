using System.Collections.Generic;
using System.IO;
using RimWorks.RimObs.Wire;
using Microsoft.Data.Sqlite;

namespace RimWorks.RimObs.Collector.Storage;

public static class SessionCatalog {
    public static IReadOnlyList<SessionMeta> List(string sessionsDir) {
        List<SessionMeta> result = new List<SessionMeta>();
        if (string.IsNullOrWhiteSpace(sessionsDir) || !Directory.Exists(sessionsDir))
            return result;

        foreach (string file in Directory.EnumerateFiles(sessionsDir, "*.db")) {
            SessionMeta? meta = TryReadMeta(file);
            if (meta != null)
                result.Add(meta);
        }

        return result;
    }

    private static SessionMeta? TryReadMeta(string dbPath) {
        try {
            using SessionStore store = SessionStore.OpenReadOnly(dbPath);
            return store.ReadFirstSessionMeta();
        }
        catch (SqliteException) {
            return null;
        }
        catch (IOException) {
            return null;
        }
    }
}
