namespace RimWorks.RimObs.Collector.Runtime;

public static class ConfigDirResolver {
    public const string EnvVarName = "RIMOBS_CONFIG_DIR";
    private const string DefaultSubdir = "RimWorks.RimObs";

    public static string Resolve(string? explicitOverride = null) =>
        Resolve(explicitOverride, static () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string Resolve(string? explicitOverride, Func<string> localAppData) {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return explicitOverride;

        string? envValue = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        // No per-user directory means no safe place to keep config. Falling back to the
        // shared temp dir would put session data somewhere any local user can rewrite.
        string baseDir = localAppData();
        if (string.IsNullOrEmpty(baseDir))
            throw new InvalidOperationException(
                "Cannot resolve a per-user config directory. Set " + EnvVarName + " to a path only you can write.");

        return Path.Combine(baseDir, DefaultSubdir);
    }
}
