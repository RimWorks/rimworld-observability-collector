using System.IO;
using RimWorks.RimObs.Collector.Cli;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleCommandTests {
    private static readonly string[] ExportWithOptionsArgs = [
        "export", "sess-123", "--output", "./out.zip",
        "--include", "allocations", "--include", "gc-events", "--force",
    ];
    private static readonly string[] ExpectedIncludes = ["allocations", "gc-events"];
    private static readonly string[] MissingSessionIdArgs = ["export", "--output", "x.zip"];
    private static readonly string[] MissingOutputArgs = ["export", "sess"];
    private static readonly string[] NoActiveSessionArgs = ["export", "sess-x", "--output", "/tmp/x.zip"];
    [Fact]
    public void ParseArgs_ExtractsExportOptions() {
        BundleCommandOptions? opts = BundleCommand.TryParseExportArgs(ExportWithOptionsArgs, out string? error);

        opts.Should().NotBeNull();
        error.Should().BeNull();
        opts!.SessionId.Should().Be("sess-123");
        opts.OutputPath.Should().Be("./out.zip");
        opts.Includes.Should().BeEquivalentTo(ExpectedIncludes);
        opts.Force.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_RejectsMissingSessionId() {
        BundleCommandOptions? opts = BundleCommand.TryParseExportArgs(MissingSessionIdArgs, out string? error);
        opts.Should().BeNull();
        error.Should().Contain("session_id");
    }

    [Fact]
    public void ParseArgs_RejectsMissingOutput() {
        BundleCommandOptions? opts = BundleCommand.TryParseExportArgs(MissingOutputArgs, out string? error);
        opts.Should().BeNull();
        error.Should().Contain("--output");
    }

    [Fact]
    public void RunExport_PrintsErrorWhenNoActiveSession() {
        StringWriter stdout = new StringWriter();
        StringWriter stderr = new StringWriter();
        int code = BundleCommand.RunExport(NoActiveSessionArgs, stdout, stderr);

        code.Should().NotBe(0);
        stderr.ToString().Should().NotBeEmpty();
    }
}
