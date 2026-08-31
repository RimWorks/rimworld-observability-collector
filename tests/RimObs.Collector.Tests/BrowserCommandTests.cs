using RimWorks.RimObs.Collector.Hosting;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class BrowserCommandTests {
    [Fact]
    public void Resolve_macos_uses_open() {
        (string fileName, IReadOnlyList<string> prefixArgs) = BrowserCommand.Resolve(BrowserPlatform.MacOS, null);
        fileName.Should().Be("open");
        prefixArgs.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_windows_uses_cmd_start() {
        (string fileName, IReadOnlyList<string> prefixArgs) = BrowserCommand.Resolve(BrowserPlatform.Windows, null);
        fileName.Should().Be("cmd");
        prefixArgs.Should().Equal("/c", "start", string.Empty);
    }

    [Fact]
    public void Resolve_linux_defaults_to_xdg_open() {
        (string fileName, IReadOnlyList<string> prefixArgs) = BrowserCommand.Resolve(BrowserPlatform.Linux, null);
        fileName.Should().Be("xdg-open");
        prefixArgs.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_linux_prefers_browser_env_when_set() {
        (string fileName, IReadOnlyList<string> prefixArgs) = BrowserCommand.Resolve(BrowserPlatform.Linux, "firefox");
        fileName.Should().Be("firefox");
        prefixArgs.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_linux_ignores_blank_browser_env() {
        (string fileName, IReadOnlyList<string> prefixArgs) = BrowserCommand.Resolve(BrowserPlatform.Linux, "   ");
        fileName.Should().Be("xdg-open");
    }
}
