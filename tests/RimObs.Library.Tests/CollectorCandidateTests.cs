using RimWorks.RimObs.Transport;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorCandidateTests {
    [Fact]
    public void ExtractPrerelease_returns_null_for_plain_release() {
        CollectorCandidate.ExtractPrerelease("1.0.0").Should().BeNull();
    }

    [Fact]
    public void ExtractPrerelease_returns_prerelease_tag() {
        CollectorCandidate.ExtractPrerelease("1.0.0-beta.1").Should().Be("beta.1");
    }

    [Fact]
    public void ExtractPrerelease_ignores_dash_inside_build_metadata() {
        CollectorCandidate.ExtractPrerelease("1.0.0+build-meta").Should().BeNull();
    }

    [Fact]
    public void ExtractPrerelease_strips_build_metadata_after_prerelease() {
        CollectorCandidate.ExtractPrerelease("1.0.0-rc.2+build-7").Should().Be("rc.2");
    }
}
