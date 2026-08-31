using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimWorks.RimObs.Collector.Runtime;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;

namespace RimWorks.RimObs.Collector.Cli;

public static class SessionsCommand {
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string? sessionsDirOverride = null, bool? outputIsRedirected = null) {
        if (args is null || args.Length == 0) {
            stderr.WriteLine("Usage: Collector sessions <list>");
            return 2;
        }

        return args[0] switch {
            "list" => RunList(args.Skip(1).ToArray(), stdout, stderr, sessionsDirOverride, outputIsRedirected),
            _ => UnknownSubcommand(args[0], stderr),
        };
    }

    private static int UnknownSubcommand(string subcommand, TextWriter stderr) {
        stderr.WriteLine($"Unknown sessions subcommand: {subcommand}");
        stderr.WriteLine("Usage: Collector sessions <list>");
        return 2;
    }

    private static int RunList(string[] args, TextWriter stdout, TextWriter stderr, string? sessionsDirOverride, bool? outputIsRedirected) {
        OutputFormat format;
        try {
            format = OutputFormatResolver.Resolve(OutputFormatResolver.ExtractFlag(args), outputIsRedirected);
        }
        catch (ArgumentException ex) {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        string sessionsDir = sessionsDirOverride ?? Path.Combine(ConfigDirResolver.Resolve(), "sessions");
        IReadOnlyList<SessionMeta> sessions = SessionCatalog.List(sessionsDir);

        if (format == OutputFormat.Json)
            WriteJson(stdout, sessions, sessionsDir);
        else
            WriteTable(stdout, sessions, sessionsDir);

        return 0;
    }

    private static void WriteJson(TextWriter stdout, IReadOnlyList<SessionMeta> sessions, string sessionsDir) {
        var payload = new {
            sessions_dir = sessionsDir,
            count = sessions.Count,
            sessions = sessions.Select(s => new {
                session_id = s.SessionId,
                started_utc_ticks = s.StartedUtcTicks,
                library_version = s.LibraryVersion,
                game_version = s.GameVersion,
            }),
        };
        stdout.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static void WriteTable(TextWriter stdout, IReadOnlyList<SessionMeta> sessions, string sessionsDir) {
        stdout.WriteLine($"Sessions directory: {sessionsDir}");
        if (sessions.Count == 0) {
            stdout.WriteLine("(no sessions)");
            return;
        }

        const string idHeader = "SESSION ID";
        const string startedHeader = "STARTED (UTC)";
        const string libHeader = "LIBRARY";
        const string gameHeader = "GAME";

        int idW = Math.Max(idHeader.Length, sessions.Max(s => s.SessionId?.Length ?? 0));
        int libW = Math.Max(libHeader.Length, sessions.Max(s => s.LibraryVersion?.Length ?? 0));
        int gameW = Math.Max(gameHeader.Length, sessions.Max(s => s.GameVersion?.Length ?? 0));

        stdout.WriteLine($"{Pad(idHeader, idW)}  {Pad(startedHeader, 20)}  {Pad(libHeader, libW)}  {Pad(gameHeader, gameW)}");
        stdout.WriteLine(new string('-', idW + 2 + 20 + 2 + libW + 2 + gameW));
        foreach (SessionMeta s in sessions.OrderByDescending(s => s.StartedUtcTicks)) {
            string started = new DateTime(s.StartedUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss");
            stdout.WriteLine($"{Pad(s.SessionId, idW)}  {Pad(started, 20)}  {Pad(s.LibraryVersion, libW)}  {Pad(s.GameVersion, gameW)}");
        }
    }

    private static string Pad(string? value, int width) => (value ?? string.Empty).PadRight(width);
}
