using RimWorks.RimObs.Collector.Comparison;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class DeltaStatusExtensionsTests {
    [Theory]
    [InlineData(DeltaStatus.Unchanged, "unchanged")]
    [InlineData(DeltaStatus.Added, "added")]
    [InlineData(DeltaStatus.Removed, "removed")]
    [InlineData(DeltaStatus.Regressed, "regressed")]
    [InlineData(DeltaStatus.Improved, "improved")]
    public void ToWireString_maps_every_status_to_its_lowercase_wire_token(DeltaStatus status, string expected) {
        status.ToWireString().Should().Be(expected);
    }
}
