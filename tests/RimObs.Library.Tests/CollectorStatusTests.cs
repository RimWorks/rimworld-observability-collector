using System.Collections.Generic;
using System.Linq;
using RimWorks.RimObs.Settings;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorStatusTests {
    private static CollectorStatus Running(int port = 17654) =>
        new(
            collectorRunning: true,
            launchAttempted: true,
            host: "127.0.0.1",
            port: port,
            controlPort: 55555,
            profilerEnabled: true,
            coreInstalled: 12,
            coreTotal: 13,
            declaredInstalled: 4,
            declaredTotal: 5,
            unresolvedCount: 1,
            failedCount: 0,
            ownerCount: 7,
            conflictCount: 0,
            gcObserverRunning: true,
            tpsFpsObserverRunning: true,
            allocationSamplerRunning: false,
            sessionId: "abc123",
            ownerId: "Author.Mod");

    [Fact]
    public void DashboardIsAvailableWhenRunningWithPort() {
        CollectorStatus status = Running(18080);

        status.DashboardAvailable.Should().BeTrue();
        status.DashboardUrl.Should().Be("http://127.0.0.1:18080/");
    }

    [Fact]
    public void DashboardUnavailableWhenNotRunning() {
        CollectorStatus status = new(
            collectorRunning: false,
            launchAttempted: true,
            host: "127.0.0.1",
            port: 18080,
            controlPort: 0,
            profilerEnabled: false,
            coreInstalled: 0,
            coreTotal: 0,
            declaredInstalled: 0,
            declaredTotal: 0,
            unresolvedCount: 0,
            failedCount: 0,
            ownerCount: 0,
            conflictCount: 0,
            gcObserverRunning: false,
            tpsFpsObserverRunning: false,
            allocationSamplerRunning: false,
            sessionId: "",
            ownerId: "");

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
        CollectorStatus status = new(
            collectorRunning: false,
            launchAttempted: false,
            host: "127.0.0.1",
            port: 0,
            controlPort: 0,
            profilerEnabled: false,
            coreInstalled: 0,
            coreTotal: 0,
            declaredInstalled: 0,
            declaredTotal: 0,
            unresolvedCount: 0,
            failedCount: 0,
            ownerCount: 0,
            conflictCount: 0,
            gcObserverRunning: false,
            tpsFpsObserverRunning: false,
            allocationSamplerRunning: false,
            sessionId: "",
            ownerId: "");

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
