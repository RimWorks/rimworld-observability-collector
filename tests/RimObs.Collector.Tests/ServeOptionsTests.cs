using RimWorks.RimObs.Collector.Hosting;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ServeOptionsTests {
    [Fact]
    public void Parse_falls_back_to_default_port_when_absent() {
        ServeOptions options = ServeOptions.Parse(["serve"], 17654);
        options.Port.Should().Be(17654);
        options.ParentPid.Should().Be(0);
        options.NoBrowser.Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_port_and_parent_pid() {
        ServeOptions options = ServeOptions.Parse(["serve", "--port", "45678", "--parent-pid", "1234"], 17654);
        options.Port.Should().Be(45678);
        options.ParentPid.Should().Be(1234);
    }

    [Fact]
    public void Parse_reads_no_browser_flag() {
        ServeOptions options = ServeOptions.Parse(["serve", "--no-browser"], 17654);
        options.NoBrowser.Should().BeTrue();
    }

    [Fact]
    public void Parse_ignores_out_of_range_port_and_keeps_default() {
        ServeOptions options = ServeOptions.Parse(["serve", "--port", "70000"], 17654);
        options.Port.Should().Be(17654);
    }

    [Fact]
    public void Parse_ignores_non_numeric_parent_pid() {
        ServeOptions options = ServeOptions.Parse(["serve", "--parent-pid", "notapid"], 17654);
        options.ParentPid.Should().Be(0);
    }
}
