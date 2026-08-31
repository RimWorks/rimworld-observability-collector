using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace RimWorks.RimObs.Collector.Bundle;

public sealed class BundleImportRegistry {
    private readonly string _baseDir;
    private readonly TimeSpan _idleTimeout;
    private readonly Func<DateTime> _nowUtc;
    private readonly ConcurrentDictionary<string, BundleImportEntry> _entries = new();

    public BundleImportRegistry(string baseDir, TimeSpan idleTimeout, Func<DateTime>? nowUtc = null) {
        _baseDir = baseDir;
        _idleTimeout = idleTimeout;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        Directory.CreateDirectory(_baseDir);
    }

    public BundleImportEntry Register(string[] contents) {
        string token = GenerateToken();
        string dir = Path.Combine(_baseDir, token);
        Directory.CreateDirectory(dir);
        BundleImportEntry entry = new BundleImportEntry {
            Token = token,
            TempDir = dir,
            Contents = contents,
            LastAccess = _nowUtc(),
        };
        _entries[token] = entry;
        return entry;
    }

    public bool TryGet(string token, out BundleImportEntry? entry) {
        bool found = _entries.TryGetValue(token, out BundleImportEntry? value);
        entry = value;
        return found;
    }

    public void Touch(string token) {
        if (_entries.TryGetValue(token, out BundleImportEntry? entry))
            entry.LastAccess = _nowUtc();
    }

    public bool Remove(string token) {
        if (!_entries.TryRemove(token, out BundleImportEntry? entry))
            return false;
        TryDelete(entry.TempDir);
        return true;
    }

    public int SweepIdle() {
        DateTime cutoff = _nowUtc() - _idleTimeout;
        int removed = 0;
        foreach (KeyValuePair<string, BundleImportEntry> pair in _entries) {
            if (pair.Value.LastAccess < cutoff && _entries.TryRemove(pair.Key, out BundleImportEntry? entry)) {
                TryDelete(entry.TempDir);
                removed++;
            }
        }
        return removed;
    }

    public void RemoveAll() {
        foreach (KeyValuePair<string, BundleImportEntry> pair in _entries) {
            if (_entries.TryRemove(pair.Key, out BundleImportEntry? entry))
                TryDelete(entry.TempDir);
        }
    }

    private static string GenerateToken() {
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    private static void TryDelete(string path) {
        try {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { /* swallow - sweep best-effort */ }
    }
}
