using RimWorks.RimObs.Collector.Security;

namespace RimWorks.RimObs.Collector.Runtime;

public static class RuntimeFiles {
    public const string TokenFileName = "collector.token";
    public const string PortFileName = "collector.port";

    public static void WriteAll(string configDir, CollectorToken token, int port) {
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Config directory must be provided.", nameof(configDir));

        Directory.CreateDirectory(configDir);

        string tokenPath = Path.Combine(configDir, TokenFileName);
        File.WriteAllText(tokenPath, token.Value);
        TryRestrictFilePermissions(tokenPath);

        string portPath = Path.Combine(configDir, PortFileName);
        File.WriteAllText(portPath, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void DeleteAll(string configDir) {
        if (string.IsNullOrWhiteSpace(configDir))
            return;

        TryDelete(Path.Combine(configDir, TokenFileName));
        TryDelete(Path.Combine(configDir, PortFileName));
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) {
            // Best-effort cleanup: the file may be locked or already gone.
        }
        catch (UnauthorizedAccessException) {
            // Best-effort cleanup: deleting without permission is non-fatal here.
        }
    }

    private static void TryRestrictFilePermissions(string path) {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;
        try {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Best-effort hardening: a filesystem that rejects chmod (network mount, restricted FS) is non-fatal.
        }
    }
}
