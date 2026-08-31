using System;
using System.IO;
using System.Text.Json;
using RimWorks.RimObs.Collector.Cli;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class CliTests {
    [Theory]
    [InlineData(new[] { "--format=json" }, "json")]
    [InlineData(new[] { "--format", "table" }, "table")]
    [InlineData(new[] { "list" }, null)]
    public void ExtractFlag_reads_format_in_both_forms(string[] args, string? expected) {
        OutputFormatResolver.ExtractFlag(args).Should().Be(expected);
    }

    [Fact]
    public void Resolve_auto_detects_json_when_output_redirected() {
        OutputFormatResolver.Resolve(null, outputIsRedirected: true).Should().Be(OutputFormat.Json);
        OutputFormatResolver.Resolve(null, outputIsRedirected: false).Should().Be(OutputFormat.Table);
    }

    [Fact]
    public void Resolve_throws_on_unknown_format() {
        Action act = () => OutputFormatResolver.Resolve("yaml");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sessions_list_reports_no_sessions_for_empty_store() {
        using TempDir dir = new TempDir();
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        int code = CliRouter.Run(["sessions", "list"], stdout, stderr, dir.Path, outputIsRedirected: false);

        code.Should().Be(0);
        stdout.ToString().Should().Contain("(no sessions)");
    }

    [Fact]
    public void Sessions_list_json_lists_stored_sessions() {
        using TempDir dir = new TempDir();
        WriteSession(dir.Path, "session-abc", "1.0.0", "1.6.4633");
        WriteSession(dir.Path, "session-def", "1.1.0", "1.6.4633");

        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        int code = CliRouter.Run(["sessions", "list", "--format=json"], stdout, stderr, dir.Path);

        code.Should().Be(0);
        using JsonDocument doc = JsonDocument.Parse(stdout.ToString());
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("sessions").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Sessions_list_table_includes_session_ids() {
        using TempDir dir = new TempDir();
        WriteSession(dir.Path, "session-table", "2.0.0", "1.6.4633");

        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        int code = CliRouter.Run(["sessions", "list"], stdout, stderr, dir.Path, outputIsRedirected: false);

        code.Should().Be(0);
        stdout.ToString().Should().Contain("session-table");
        stdout.ToString().Should().Contain("SESSION ID");
    }

    [Fact]
    public void Bundle_export_routes_to_BundleCommand() {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        int code = CliRouter.Run(["bundle"], stdout, stderr);

        code.Should().Be(2);
        stderr.ToString().Should().Contain("bundle export");
    }

    [Fact]
    public void Unknown_command_returns_usage_error() {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        int code = CliRouter.Run(["frobnicate"], stdout, stderr);

        code.Should().Be(2);
        stderr.ToString().Should().Contain("Unknown command");
    }

    [Fact]
    public void Version_command_prints_revision() {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();

        int code = CliRouter.Run(["version"], stdout, stderr);

        code.Should().Be(0);
        stdout.ToString().Should().Contain(BuildInfo.Revision);
    }

    private static void WriteSession(string sessionsDir, string id, string libVersion, string gameVersion) {
        string dbPath = Path.Combine(sessionsDir, id + ".db");
        using SessionStore store = SessionStore.Open(dbPath);
        store.WriteSessionMeta(new SessionMeta {
            SessionId = id,
            StartedUtcTicks = DateTime.UtcNow.Ticks,
            StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
            AnchorTimestamp = 0,
            LibraryVersion = libVersion,
            GameVersion = gameVersion,
        });
    }

    private sealed class TempDir : IDisposable {
        public TempDir() {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rimobs-cli-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() {
            try {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException) {
            }
        }
    }
}
