using RimWorks.RimObs.Settings;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class StatusLineTests {
    [Fact]
    public void ConstructorAssignsAllProperties() {
        StatusLine line = new("Collector", "running", healthy: true);

        line.Label.Should().Be("Collector");
        line.Value.Should().Be("running");
        line.Healthy.Should().BeTrue();
    }

    [Fact]
    public void UnhealthyFlagIsPropagated() {
        StatusLine line = new("Sections", "0/13", healthy: false);

        line.Healthy.Should().BeFalse();
    }
}
