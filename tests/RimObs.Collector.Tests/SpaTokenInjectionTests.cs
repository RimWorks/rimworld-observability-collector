using RimWorks.RimObs.Collector.Api;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SpaTokenInjectionTests {
    [Fact]
    public void InjectToken_inserts_token_global_before_head_close() {
        string html = "<html><head><title>x</title></head><body></body></html>";

        string result = SpaEndpoints.InjectToken(html, "secret-token");

        result.Should().Contain("window.__RIMOBS_TOKEN__ = \"secret-token\";");
        result.IndexOf("window.__RIMOBS_TOKEN__", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectToken_json_escapes_special_characters() {
        string html = "<head></head>";

        string result = SpaEndpoints.InjectToken(html, "a\"b</script>");

        result.Should().Contain("window.__RIMOBS_TOKEN__ = \"a\\u0022b\\u003C/script\\u003E\";");
    }

    [Fact]
    public void InjectToken_returns_html_unchanged_when_no_head_close() {
        string html = "<html><body>no head</body></html>";

        string result = SpaEndpoints.InjectToken(html, "secret-token");

        result.Should().Be(html);
    }
}
