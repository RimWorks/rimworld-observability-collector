using RimWorks.RimObs.Collector.Runtime;
using RimWorks.RimObs.Collector.Security;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class RuntimeFilesTests : IDisposable {
    private readonly string _tempDir;

    public RuntimeFilesTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "rimobs-runtime-files-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void WriteAll_creates_directory_and_writes_token_and_port() {
        CollectorToken token = CollectorToken.FromExplicitValue("my-secret-token");
        RuntimeFiles.WriteAll(_tempDir, token, port: 17654);

        Directory.Exists(_tempDir).Should().BeTrue();
        string tokenPath = Path.Combine(_tempDir, RuntimeFiles.TokenFileName);
        string portPath = Path.Combine(_tempDir, RuntimeFiles.PortFileName);
        File.Exists(tokenPath).Should().BeTrue();
        File.Exists(portPath).Should().BeTrue();
        File.ReadAllText(tokenPath).Should().Be("my-secret-token");
        File.ReadAllText(portPath).Should().Be("17654");
    }

    [Fact]
    public void WriteAll_overwrites_existing_files() {
        Directory.CreateDirectory(_tempDir);
        string tokenPath = Path.Combine(_tempDir, RuntimeFiles.TokenFileName);
        File.WriteAllText(tokenPath, "stale-token-from-previous-run");

        CollectorToken token = CollectorToken.FromExplicitValue("fresh-token");
        RuntimeFiles.WriteAll(_tempDir, token, port: 17654);

        File.ReadAllText(tokenPath).Should().Be("fresh-token");
    }

    [Fact]
    public void WriteAll_rejects_empty_directory() {
        CollectorToken token = CollectorToken.FromExplicitValue("any");
        Action act = () => RuntimeFiles.WriteAll("   ", token, port: 17654);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteAll_sets_owner_only_permissions_on_unix() {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        CollectorToken token = CollectorToken.FromExplicitValue("perm-token");
        RuntimeFiles.WriteAll(_tempDir, token, port: 17654);

        string tokenPath = Path.Combine(_tempDir, RuntimeFiles.TokenFileName);
        UnixFileMode mode = File.GetUnixFileMode(tokenPath);
        mode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
