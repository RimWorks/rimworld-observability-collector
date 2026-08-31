using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class PlaceholderTests {
    [Fact]
    public void TelemetryBatch_default_state_is_sane() {
        TelemetryBatch batch = new();
        batch.SchemaVersion.Should().Be(0);
        batch.Sequence.Should().Be(0UL);
        batch.OwnerId.Should().Be(string.Empty);
        batch.Payload.Should().BeEmpty();
    }
}
