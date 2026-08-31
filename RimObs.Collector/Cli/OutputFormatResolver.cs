using System;

namespace RimWorks.RimObs.Collector.Cli;

public static class OutputFormatResolver {
    public const string FlagName = "--format";

    public static OutputFormat Resolve(string? explicitFormat, bool? outputIsRedirected = null) {
        if (!string.IsNullOrWhiteSpace(explicitFormat)) {
            string normalized = explicitFormat.Trim().ToLowerInvariant();
            return normalized switch {
                "table" => OutputFormat.Table,
                "json" => OutputFormat.Json,
                _ => throw new ArgumentException($"Unknown {FlagName} value: {explicitFormat}. Supported: table, json."),
            };
        }

        bool redirected = outputIsRedirected ?? Console.IsOutputRedirected;
        return redirected ? OutputFormat.Json : OutputFormat.Table;
    }

    public static string? ExtractFlag(string[] args) {
        if (args is null)
            return null;
        for (int i = 0; i < args.Length; i++) {
            string a = args[i];
            if (a.StartsWith(FlagName + "=", StringComparison.Ordinal))
                return a.Substring(FlagName.Length + 1);
            if (a == FlagName && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }
}
