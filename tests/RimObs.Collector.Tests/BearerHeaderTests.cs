using RimWorks.RimObs.Collector.Security;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class BearerHeaderTests {
    [Theory]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("bearer abc123", "abc123")]
    [InlineData("BEARER abc123", "abc123")]
    [InlineData("Bearer  abc123  ", "abc123")]
    [InlineData("Bearer ", null)]
    [InlineData("Bearer", null)]
    [InlineData("Basic abc123", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractToken_pulls_value_only_for_bearer_scheme(string? header, string? expected) {
        BearerHeader.ExtractToken(header).Should().Be(expected);
    }
}
