namespace RimWorks.RimObs.Collector.Runtime;

public static class ConfigDirResolver {
    public const string EnvVarName = "RIMOBS_CONFIG_DIR";
    private const string DefaultSubdir = "RimWorks.RimObs";

    public static string Resolve(string? explicitOverride = null) {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return explicitOverride;

        string? envValue = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.GetTempPath();
        return Path.Combine(baseDir, DefaultSubdir);
    }
}
