using System.Collections.Concurrent;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Storage;

public sealed class SqliteSessionPersister : ISessionPersister {
    private readonly string _sessionsDir;
    private readonly ConcurrentDictionary<string, SessionStore> _stores = new(StringComparer.Ordinal);
    private bool _disposed;

    public SqliteSessionPersister(string sessionsDir) {
        if (string.IsNullOrWhiteSpace(sessionsDir))
            throw new ArgumentException("sessionsDir must be non-empty", nameof(sessionsDir));

        _sessionsDir = sessionsDir;
        Directory.CreateDirectory(_sessionsDir);
    }

    public string SessionsDirectory => _sessionsDir;

    public string ResolveDatabasePath(string sessionId) {
        ValidateSessionId(sessionId);
        return Path.Combine(_sessionsDir, SanitizeFileName(sessionId) + ".db");
    }

    public void WriteSessionMeta(SessionMeta meta) {
        ArgumentNullException.ThrowIfNull(meta);
        if (string.IsNullOrWhiteSpace(meta.SessionId))
            throw new ArgumentException("SessionMeta.SessionId must be non-empty", nameof(meta));
        ThrowIfDisposed();

        SessionStore store = GetOrOpen(meta.SessionId);
        store.WriteSessionMeta(meta);
    }


    public void WriteSectionsSnapshot(string sessionId, IReadOnlyCollection<SectionStats> sections) {
        ArgumentNullException.ThrowIfNull(sections);
        ValidateSessionId(sessionId);
        if (sections.Count == 0)
            return;
        ThrowIfDisposed();

        SessionStore store = GetOrOpen(sessionId);
        store.WriteSectionsSnapshot(sections);
    }

    public void WriteMetricsSnapshot(string sessionId, IReadOnlyCollection<MetricStats> metrics) {
        ArgumentNullException.ThrowIfNull(metrics);
        ValidateSessionId(sessionId);
        if (metrics.Count == 0)
            return;
        ThrowIfDisposed();

        SessionStore store = GetOrOpen(sessionId);
        store.WriteMetricsSnapshot(metrics);
    }

    public void WriteCallTreeSnapshot(string sessionId, IReadOnlyCollection<CallEdgeStats> edges) {
        ArgumentNullException.ThrowIfNull(edges);
        ValidateSessionId(sessionId);
        if (edges.Count == 0)
            return;
        ThrowIfDisposed();

        SessionStore store = GetOrOpen(sessionId);
        store.WriteCallTreeSnapshot(edges);
    }

    public void ReplaceGcEventsSnapshot(string sessionId, GcEventRecord[] events) {
        ArgumentNullException.ThrowIfNull(events);
        ValidateSessionId(sessionId);
        ThrowIfDisposed();

        SessionStore store = GetOrOpen(sessionId);
        store.ReplaceGcEventsSnapshot(events);
    }

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        foreach (SessionStore store in _stores.Values)
            store.Dispose();
        _stores.Clear();
    }

    private SessionStore GetOrOpen(string sessionId) {
        return _stores.GetOrAdd(sessionId, id => {
            string dbPath = Path.Combine(_sessionsDir, SanitizeFileName(id) + ".db");
            return SessionStore.Open(dbPath);
        });
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }


    private static void ValidateSessionId(string sessionId) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId must be non-empty", nameof(sessionId));
    }

    private static string SanitizeFileName(string raw) {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = raw;
        foreach (char ch in invalid)
            sanitized = sanitized.Replace(ch, '_');
        return sanitized;
    }
}
