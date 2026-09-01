using System.Collections.Generic;
using System.Linq;
using RimWorks.RimObs.Settings;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorStatusTests {
    private static CollectorStatus Running(int port = 17654) =>
        new() {
            CollectorRunning = true,
            LaunchAttempted = true,
            Host = "127.0.0.1",
            Port = port,
            ControlPort = 55555,
            ProfilerEnabled = true,
            CoreInstalled = 12,
            CoreTotal = 13,
            DeclaredInstalled = 4,
            DeclaredTotal = 5,
            UnresolvedCount = 1,
            FailedCount = 0,
            OwnerCount = 7,
            ConflictCount = 0,
            GcObserverRunning = true,
            TpsFpsObserverRunning = true,
            AllocationSamplerRunning = false,
            SessionId = "abc123",
            OwnerId = "Author.Mod",
        };

    [Fact]
    public void DashboardIsAvailableWhenRunningWithPort() {
        CollectorStatus status = Running(18080);

        status.DashboardAvailable.Should().BeTrue();
        status.DashboardUrl.Should().Be("http://127.0.0.1:18080/");
    }

    [Fact]
    public void DashboardUnavailableWhenNotRunning() {
        CollectorStatus status = new() {
            CollectorRunning = false,
            LaunchAttempted = true,
            Host = "127.0.0.1",
            Port = 18080,
            ControlPort = 0,
            ProfilerEnabled = false,
            CoreInstalled = 0,
            CoreTotal = 0,
            DeclaredInstalled = 0,
            DeclaredTotal = 0,
            UnresolvedCount = 0,
            FailedCount = 0,
            OwnerCount = 0,
            ConflictCount = 0,
            GcObserverRunning = false,
            TpsFpsObserverRunning = false,
            AllocationSamplerRunning = false,
            SessionId = "",
            OwnerId = "",
        };

        status.DashboardAvailable.Should().BeFalse();
    }

    [Fact]
    public void DashboardUrlEmptyWhenPortUnallocated() {
        CollectorStatus status = Running(0);

        status.DashboardUrl.Should().BeEmpty();
        status.DashboardAvailable.Should().BeFalse();
    }

    [Fact]
    public void BuildLinesReportsRunningCollectorAsHealthy() {
        IReadOnlyList<StatusLine> lines = Running(17654).BuildLines();

        StatusLine collector = lines.Single(l => l.Label == "Collector");
        collector.Healthy.Should().BeTrue();
        collector.Value.Should().Contain("17654");
    }

    [Fact]
    public void BuildLinesFlagsNotRunningCollectorAsUnhealthy() {
        CollectorStatus status = new() {
            CollectorRunning = false,
            LaunchAttempted = false,
            Host = "127.0.0.1",
            Port = 0,
            ControlPort = 0,
            ProfilerEnabled = false,
            CoreInstalled = 0,
            CoreTotal = 0,
            DeclaredInstalled = 0,
            DeclaredTotal = 0,
            UnresolvedCount = 0,
            FailedCount = 0,
            OwnerCount = 0,
            ConflictCount = 0,
            GcObserverRunning = false,
            TpsFpsObserverRunning = false,
            AllocationSamplerRunning = false,
            SessionId = "",
            OwnerId = "",
        };

        StatusLine collector = status.BuildLines().Single(l => l.Label == "Collector");
        collector.Healthy.Should().BeFalse();
        collector.Value.Should().Be("not running");
    }

    [Fact]
    public void BuildLinesReportsCoreSectionCounts() {
        StatusLine core = Running().BuildLines().Single(l => l.Label == "Core sections");

        core.Value.Should().Be("12/13 installed (unresolved=1, failed=0)");
        core.Healthy.Should().BeFalse();
    }

    [Fact]
    public void BuildLinesMarksOptInAllocationSamplerAsHealthyWhenOff() {
        StatusLine sampler = Running().BuildLines().Single(l => l.Label == "Allocation sampler");

        sampler.Value.Should().Be("off (opt-in)");
        sampler.Healthy.Should().BeTrue();
    }
}
