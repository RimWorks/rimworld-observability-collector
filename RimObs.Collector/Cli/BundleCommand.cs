using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

namespace RimWorks.RimObs.Collector.Cli;

public sealed class BundleCommandOptions {
    public string SessionId { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<string> Includes { get; set; } = new();
    public bool Force { get; set; }
    public string ServerUrl { get; set; } = "http://127.0.0.1:17654";
}

public static class BundleCommand {
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) {
        if (args.Length == 0 || args[0] != "export") {
            stderr.WriteLine("Usage: Collector bundle export <session_id> --output <path> [--include <key>]... [--force]");
            return 2;
        }
        return RunExport(args, stdout, stderr);
    }

    public static int RunExport(string[] args, TextWriter stdout, TextWriter stderr) {
        BundleCommandOptions? opts = TryParseExportArgs(args, out string? error);
        if (opts is null) {
            stderr.WriteLine($"error: {error}");
            return 2;
        }

        try {
            using HttpClient client = new HttpClient { BaseAddress = new Uri(opts.ServerUrl) };
            client.DefaultRequestHeaders.Add("Origin", opts.ServerUrl);
            HttpResponseMessage response = client.PostAsJsonAsync("/api/v1/export/bundle", new {
                session_id = opts.SessionId,
                include = opts.Includes.ToArray(),
                force = opts.Force,
            }).GetAwaiter().GetResult();

            if ((int)response.StatusCode == 413) {
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                stderr.WriteLine($"error: bundle exceeds 25MB soft cap. Retry with --force. ({body})");
                return 2;
            }
            if (!response.IsSuccessStatusCode) {
                stderr.WriteLine($"error: server returned {(int)response.StatusCode}");
                return 1;
            }
            byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            File.WriteAllBytes(opts.OutputPath, bytes);
            stdout.WriteLine($"wrote {bytes.Length} bytes to {opts.OutputPath}");
            return 0;
        }
        catch (HttpRequestException ex) {
            stderr.WriteLine($"error: cannot reach collector at {opts.ServerUrl} ({ex.Message})");
            return 1;
        }
    }

    public static BundleCommandOptions? TryParseExportArgs(string[] args, out string? error) {
        error = null;
        BundleCommandOptions opts = new BundleCommandOptions();
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) {
            error = "missing session_id";
            return null;
        }
        opts.SessionId = args[1];
        int i = 2;
        while (i < args.Length) {
            string a = args[i];
            switch (a) {
                case "--output":
                    if (i + 1 >= args.Length) { error = "--output requires a value"; return null; }
                    opts.OutputPath = args[++i];
                    break;
                case "--include":
                    if (i + 1 >= args.Length) { error = "--include requires a value"; return null; }
                    opts.Includes.Add(args[++i]);
                    break;
                case "--force":
                    opts.Force = true;
                    break;
                case "--server":
                    if (i + 1 >= args.Length) { error = "--server requires a value"; return null; }
                    opts.ServerUrl = args[++i];
                    break;
                default:
                    error = $"unknown argument: {a}";
                    return null;
            }
            i++;
        }
        if (string.IsNullOrEmpty(opts.OutputPath)) {
            error = "--output is required";
            return null;
        }
        return opts;
    }
}
