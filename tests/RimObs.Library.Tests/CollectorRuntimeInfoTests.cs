using RimWorks.RimObs.Settings;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorRuntimeInfoTests : IDisposable {
    public CollectorRuntimeInfoTests() => CollectorRuntimeInfo.ResetForTests();

    public void Dispose() => CollectorRuntimeInfo.ResetForTests();

    [Fact]
    public void SetPopulatesAllFields() {
        CollectorRuntimeInfo.Set("10.0.0.5", 17654, collectorRunning: true, launchAttempted: true, "Author.Mod");

        CollectorRuntimeInfo.Host.Should().Be("10.0.0.5");
        CollectorRuntimeInfo.Port.Should().Be(17654);
        CollectorRuntimeInfo.CollectorRunning.Should().BeTrue();
        CollectorRuntimeInfo.LaunchAttempted.Should().BeTrue();
        CollectorRuntimeInfo.OwnerId.Should().Be("Author.Mod");
    }

    [Fact]
    public void SetFallsBackToLoopbackWhenHostIsNullOrEmpty() {
        CollectorRuntimeInfo.Set(null!, 1, collectorRunning: false, launchAttempted: false, "owner");
        CollectorRuntimeInfo.Host.Should().Be("127.0.0.1");

        CollectorRuntimeInfo.Set(string.Empty, 1, collectorRunning: false, launchAttempted: false, "owner");
        CollectorRuntimeInfo.Host.Should().Be("127.0.0.1");
    }

    [Fact]
    public void SetCoercesNullOwnerIdToEmptyString() {
        CollectorRuntimeInfo.Set("127.0.0.1", 1, collectorRunning: false, launchAttempted: false, null!);
        CollectorRuntimeInfo.OwnerId.Should().Be(string.Empty);
    }

    [Fact]
    public void ResetForTestsClearsState() {
        CollectorRuntimeInfo.Set("10.0.0.5", 17654, collectorRunning: true, launchAttempted: true, "Author.Mod");

        CollectorRuntimeInfo.ResetForTests();

        CollectorRuntimeInfo.Host.Should().Be("127.0.0.1");
        CollectorRuntimeInfo.Port.Should().Be(0);
        CollectorRuntimeInfo.CollectorRunning.Should().BeFalse();
        CollectorRuntimeInfo.LaunchAttempted.Should().BeFalse();
        CollectorRuntimeInfo.OwnerId.Should().Be(string.Empty);
    }

    [Fact]
    public void DashboardUrlUsesTheHostAndPort() {
        CollectorRuntimeInfo.Set("127.0.0.1", 46075, collectorRunning: true, launchAttempted: true, "owner");

        CollectorRuntimeInfo.DashboardUrl.Should().Be("http://127.0.0.1:46075/");
    }

    // regression: the URL only ever reached the collector's own stdout, so nobody reading
    // RimWorld's in-game debug log could find the dashboard.
    [Fact]
    public void DashboardUrlIsEmptyBeforeAPortIsAllocated() {
        CollectorRuntimeInfo.DashboardUrl.Should().BeEmpty();
    }

    [Fact]
    public void DashboardUrlIsEmptyWhenNoCollectorIsRunning() {
        CollectorRuntimeInfo.Set("127.0.0.1", 46075, collectorRunning: false, launchAttempted: true, "owner");

        CollectorRuntimeInfo.DashboardUrl.Should().BeEmpty();
    }
}
