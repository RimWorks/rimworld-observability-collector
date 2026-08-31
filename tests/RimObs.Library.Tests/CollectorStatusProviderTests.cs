using System;
using RimWorks.RimObs.Settings;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorStatusProviderTests : IDisposable {
    public CollectorStatusProviderTests() => CollectorRuntimeInfo.ResetForTests();

    public void Dispose() => CollectorRuntimeInfo.ResetForTests();

    [Fact]
    public void CaptureReflectsRuntimeInfo() {
        CollectorRuntimeInfo.Set("127.0.0.1", 19001, collectorRunning: true, launchAttempted: true, "Author.Mod");

        CollectorStatus status = CollectorStatusProvider.CaptureCurrent();

        status.CollectorRunning.Should().BeTrue();
        status.LaunchAttempted.Should().BeTrue();
        status.Host.Should().Be("127.0.0.1");
        status.Port.Should().Be(19001);
        status.OwnerId.Should().Be("Author.Mod");
        status.DashboardUrl.Should().Be("http://127.0.0.1:19001/");
        status.DashboardAvailable.Should().BeTrue();
    }

    [Fact]
    public void CaptureWithNoRuntimeInfoReportsUnavailable() {
        CollectorStatus status = CollectorStatusProvider.CaptureCurrent();

        status.CollectorRunning.Should().BeFalse();
        status.Port.Should().Be(0);
        status.DashboardAvailable.Should().BeFalse();
    }
}
