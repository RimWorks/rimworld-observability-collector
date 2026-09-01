using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimWorks.RimObs.Collector.Bundle;

public enum BundleImportStatus {
    Ok,
    InvalidArchive,
    MissingManifest,
}

public sealed class BundleImportResult {
    public BundleImportStatus Status { get; init; }
    public BundleImportEntry? Entry { get; init; }
    public BundleManifest? Manifest { get; init; }
}

public sealed class BundleImportService {
    private readonly BundleImportRegistry _registry;

    public BundleImportService(BundleImportRegistry registry) {
        _registry = registry;
    }

    public async Task<BundleImportResult> ImportAsync(Stream archiveStream) {
        List<string> names = new List<string>();
        BundleImportEntry? entry = null;

        try {
            using ZipArchive zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

            if (!TryCollectEntryNames(zip, names))
                return new BundleImportResult { Status = BundleImportStatus.InvalidArchive };

            if (!names.Contains("manifest.json"))
                return new BundleImportResult { Status = BundleImportStatus.MissingManifest };

            entry = _registry.Register(names.ToArray());

            if (!await TryExtractEntries(zip, entry)) {
                _registry.Remove(entry.Token);
                return new BundleImportResult { Status = BundleImportStatus.InvalidArchive };
            }

            string manifestPath = Path.Combine(entry.TempDir, "manifest.json");
            using FileStream mfs = File.OpenRead(manifestPath);
            BundleManifest? manifest = await JsonSerializer.DeserializeAsync<BundleManifest>(mfs, BundleManifest.JsonOptions);

            return new BundleImportResult {
                Status = BundleImportStatus.Ok,
                Entry = entry,
                Manifest = manifest,
            };
        }
        catch (InvalidDataException) {
            if (entry is not null) _registry.Remove(entry.Token);
            return new BundleImportResult { Status = BundleImportStatus.InvalidArchive };
        }
    }

    private static bool TryCollectEntryNames(ZipArchive zip, List<string> names) {
        foreach (ZipArchiveEntry e in zip.Entries) {
            if (string.IsNullOrEmpty(e.Name)) continue;
            if (e.FullName != e.Name)
                return false;
            if (e.FullName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(e.FullName))
                return false;
            names.Add(e.FullName);
        }
        return true;
    }

    private static async Task<bool> TryExtractEntries(ZipArchive zip, BundleImportEntry entry) {
        string root = Path.GetFullPath(entry.TempDir) + Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry e in zip.Entries) {
            if (string.IsNullOrEmpty(e.Name)) continue;
            string destPath = Path.GetFullPath(Path.Combine(entry.TempDir, e.FullName));
            if (!destPath.StartsWith(root, StringComparison.Ordinal))
                return false;
            string? destDir = Path.GetDirectoryName(destPath);
            if (destDir is not null) Directory.CreateDirectory(destDir);
            await using Stream src = await e.OpenAsync();
            // File.Create hands back a synchronous stream, so CopyToAsync would block a
            // thread pool thread per entry. useAsync: true makes the copy really async.
            await using FileStream dst = new(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true);
            await src.CopyToAsync(dst);
        }
        return true;
    }
}
