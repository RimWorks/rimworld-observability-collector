using System;
using System.IO;
using RimWorks.RimObs.Collector.Bundle;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class ReportHtmlBuilderTests {
    [Fact]
    public void Inject_PrependsScriptBeforeHeadClose() {
        string template = "<!doctype html><html><head><title>x</title></head><body></body></html>";
        string payload = "{\"manifest\":{\"session_id\":\"abc\"}}";

        string output = ReportHtmlBuilder.InjectBundle(template, payload);

        output.Should().Contain("window.__BUNDLE__ = {\"manifest\":{\"session_id\":\"abc\"}};");
        output.IndexOf("window.__BUNDLE__").Should().BeLessThan(output.IndexOf("</head>"));
    }

    [Fact]
    public void Inject_EscapesScriptTagsInPayload() {
        string template = "<head></head>";
        string payload = "{\"x\":\"</script>\"}";

        string output = ReportHtmlBuilder.InjectBundle(template, payload);

        output.Should().NotContain("\"</script>\"");
        output.Should().Contain("\\u003C/script\\u003E");
    }

    [Fact]
    public void Inject_ThrowsWhenTemplateLacksHead() {
        string template = "<html><body></body></html>";
        Action act = () => ReportHtmlBuilder.InjectBundle(template, "{}");
        act.Should().Throw<InvalidOperationException>();
    }
}
