using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Update;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class UpdateCheckerTests {
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V2.0.0", 2, 0, 0, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.4.0+build.5", 1, 4, 0, null)]
    [InlineData("v1.4.0-rc.2+build.99", 1, 4, 0, "rc.2")]
    public void TryParse_accepts_valid_semver(string input, int major, int minor, int patch, string? pre) {
        SemVer.TryParse(input, out SemVer? parsed).Should().BeTrue();
        parsed.Should().NotBeNull();
        parsed!.Core.Major.Should().Be(major);
        parsed.Core.Minor.Should().Be(minor);
        parsed.Core.Build.Should().Be(patch);
        parsed.Prerelease.Should().Be(pre);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a version")]
    [InlineData("1.2.3-")]
    [InlineData("v")]
    public void TryParse_rejects_bad_input(string input) {
        SemVer.TryParse(input, out SemVer? _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_null() {
        SemVer.TryParse(null, out SemVer? _).Should().BeFalse();
    }

    [Fact]
    public void Compare_orders_by_core_version() {
        SemVer.Compare(Parse("1.2.3"), Parse("1.2.4")).Should().BeLessThan(0);
        SemVer.Compare(Parse("2.0.0"), Parse("1.9.9")).Should().BeGreaterThan(0);
        SemVer.Compare(Parse("1.2.3"), Parse("1.2.3")).Should().Be(0);
    }

    [Fact]
    public void Compare_prefers_stable_over_prerelease_at_same_core() {
        SemVer.Compare(Parse("1.2.3"), Parse("1.2.3-beta.1")).Should().BeGreaterThan(0);
        SemVer.Compare(Parse("1.2.3-beta.1"), Parse("1.2.3")).Should().BeLessThan(0);
    }

    [Fact]
    public void Compare_orders_prereleases_ordinally_at_same_core() {
        SemVer.Compare(Parse("1.2.3-alpha"), Parse("1.2.3-beta")).Should().BeLessThan(0);
    }

    [Fact]
    public void SelectUpdate_returns_null_for_empty_or_only_older_releases() {
        UpdateChecker.SelectUpdate("1.5.0", Array.Empty<ReleaseInfo>()).Should().BeNull();

        List<ReleaseInfo> older = new() {
            new ReleaseInfo { TagName = "v1.4.0" },
            new ReleaseInfo { TagName = "v1.3.0" },
        };
        UpdateChecker.SelectUpdate("1.5.0", older).Should().BeNull();
    }

    [Fact]
    public void SelectUpdate_picks_highest_stable_release_strictly_newer() {
        List<ReleaseInfo> releases = new() {
            new ReleaseInfo { TagName = "v1.5.0" },
            new ReleaseInfo { TagName = "v1.6.0" },
            new ReleaseInfo { TagName = "v1.7.0" },
            new ReleaseInfo { TagName = "v1.4.0" },
        };

        ReleaseInfo? result = UpdateChecker.SelectUpdate("1.5.0", releases);

        result.Should().NotBeNull();
        result!.TagName.Should().Be("v1.7.0");
    }

    [Fact]
    public void SelectUpdate_ignores_prerelease_flag_and_prerelease_tags() {
        List<ReleaseInfo> releases = new() {
            new ReleaseInfo { TagName = "v2.0.0", Prerelease = true },
            new ReleaseInfo { TagName = "v1.9.0-rc.1" },
            new ReleaseInfo { TagName = "v1.6.0" },
        };

        ReleaseInfo? result = UpdateChecker.SelectUpdate("1.5.0", releases);

        result.Should().NotBeNull();
        result!.TagName.Should().Be("v1.6.0");
    }

    [Fact]
    public void SelectUpdate_ignores_drafts() {
        List<ReleaseInfo> releases = new() {
            new ReleaseInfo { TagName = "v2.0.0", Draft = true },
            new ReleaseInfo { TagName = "v1.6.0" },
        };

        UpdateChecker.SelectUpdate("1.5.0", releases)!.TagName.Should().Be("v1.6.0");
    }

    [Fact]
    public void SelectUpdate_skips_malformed_tags_without_failing() {
        List<ReleaseInfo> releases = new() {
            new ReleaseInfo { TagName = "garbage" },
            new ReleaseInfo { TagName = "v1.7.0" },
            new ReleaseInfo { TagName = "" },
        };

        UpdateChecker.SelectUpdate("1.5.0", releases)!.TagName.Should().Be("v1.7.0");
    }

    [Fact]
    public void SelectUpdate_offers_stable_when_current_is_prerelease_of_same_core() {
        List<ReleaseInfo> releases = new() {
            new ReleaseInfo { TagName = "v1.5.0" },
        };

        ReleaseInfo? result = UpdateChecker.SelectUpdate("1.5.0-beta.1", releases);

        result.Should().NotBeNull();
        result!.TagName.Should().Be("v1.5.0");
    }

    [Fact]
    public void SelectUpdate_returns_null_when_current_version_is_unparseable() {
        List<ReleaseInfo> releases = new() {
            new ReleaseInfo { TagName = "v9.9.9" },
        };

        UpdateChecker.SelectUpdate("not-a-version", releases).Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_returns_null_on_non_success_status() {
        StubHandler handler = new(req => new HttpResponseMessage(HttpStatusCode.NotFound));
        using HttpClient client = new(handler);

        ReleaseInfo? result = await UpdateChecker
            .CheckAsync(client, "1.0.0", "owner", "repo", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_returns_newest_stable_from_api_payload() {
        const string body = """
            [
              { "tag_name": "v1.5.0", "html_url": "https://example/v15", "prerelease": false, "draft": false },
              { "tag_name": "v2.0.0", "html_url": "https://example/v20", "prerelease": false, "draft": false },
              { "tag_name": "v2.1.0-rc.1", "html_url": "https://example/v21rc", "prerelease": true, "draft": false }
            ]
            """;
        StubHandler handler = new(req => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
        using HttpClient client = new(handler);

        ReleaseInfo? result = await UpdateChecker
            .CheckAsync(client, "1.4.0", "owner", "repo", CancellationToken.None);

        result.Should().NotBeNull();
        result!.TagName.Should().Be("v2.0.0");
        result.HtmlUrl.Should().Be("https://example/v20");
    }

    [Fact]
    public async Task CheckAsync_sends_request_with_github_headers() {
        HttpRequestMessage? captured = null;
        StubHandler handler = new(req => {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            };
        });
        using HttpClient client = new(handler);

        await UpdateChecker.CheckAsync(client, "1.0.0", "owner", "repo", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RequestUri!.ToString().Should().Contain("/repos/owner/repo/releases");
        captured.Headers.UserAgent.ToString().Should().Contain("RimObs");
        captured.Headers.Accept.ToString().Should().Contain("github");
    }

    private static SemVer Parse(string s) {
        SemVer.TryParse(s, out SemVer? v).Should().BeTrue($"input '{s}' should parse");
        return v!;
    }

    private sealed class StubHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            return Task.FromResult(_responder(request));
        }
    }
}
