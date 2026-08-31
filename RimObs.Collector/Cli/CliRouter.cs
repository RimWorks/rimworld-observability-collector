using System;
using System.IO;

namespace RimWorks.RimObs.Collector.Cli;

public static class CliRouter {
    public static int Run(string[] args) {
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string? sessionsDirOverride = null, bool? outputIsRedirected = null) {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h") {
            PrintHelp(stdout);
            return 0;
        }

        if (args[0] == "version" || args[0] == "--version") {
            stdout.WriteLine($"{Wire.BuildInfo.Revision} (built {Wire.BuildInfo.BuildTime})");
            return 0;
        }

        if (args[0] == "sessions") {
            return SessionsCommand.Run(args.AsSpan(1).ToArray(), stdout, stderr, sessionsDirOverride, outputIsRedirected);
        }

        if (args[0] == "bundle") {
            return BundleCommand.Run(args.AsSpan(1).ToArray(), stdout, stderr);
        }

        stderr.WriteLine($"Unknown command: {args[0]}");
        PrintHelp(stderr);
        return 2;
    }

    private static void PrintHelp(TextWriter writer) {
        writer.WriteLine("Collector — RimWorld Observability Collector");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  Collector serve                       Start the daemon and dashboard.");
        writer.WriteLine("  Collector sessions list [--format=table|json]");
        writer.WriteLine("                                        List sessions in the local store.");
        writer.WriteLine("  Collector bundle export <id> --output <path> [--include <key>]... [--force]");
        writer.WriteLine("                                        Export a diagnostic bundle for the given session.");
        writer.WriteLine("  Collector version                     Print version and exit.");
        writer.WriteLine("  Collector --help                      Show this help.");
    }
}
