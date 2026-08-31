using System.IO;
using RimWorks.RimObs.Collector.Cli;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class CliRouterTests {
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_flag_prints_usage_and_returns_zero(string flag) {
        (int code, string stdout, string _) = CaptureRun([flag]);

        code.Should().Be(0);
        stdout.Should().Contain("Usage:");
        stdout.Should().Contain("Collector serve");
    }

    [Fact]
    public void No_args_prints_help_and_returns_zero() {
        (int code, string stdout, string _) = CaptureRun([]);

        code.Should().Be(0);
        stdout.Should().Contain("Usage:");
    }

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public void Version_command_prints_revision(string arg) {
        (int code, string stdout, string _) = CaptureRun([arg]);

        code.Should().Be(0);
        stdout.Should().Contain("built");
    }

    [Fact]
    public void Unknown_command_returns_exit_two_and_writes_error() {
        (int code, string _, string stderr) = CaptureRun(["nope"]);

        code.Should().Be(2);
        stderr.Should().Contain("Unknown command: nope");
    }

    private static (int Code, string Stdout, string Stderr) CaptureRun(string[] args) {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        using StringWriter outWriter = new();
        using StringWriter errWriter = new();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            int code = CliRouter.Run(args);
            return (code, outWriter.ToString(), errWriter.ToString());
        }
        finally {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
