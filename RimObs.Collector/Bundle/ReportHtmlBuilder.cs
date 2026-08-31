using System;
using System.IO;
using System.Reflection;

namespace RimWorks.RimObs.Collector.Bundle;

public static class ReportHtmlBuilder {
    private const string EmbeddedResourceName = "Dashboard.report.html";

    public static string LoadTemplate() {
        Assembly assembly = typeof(ReportHtmlBuilder).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' not found. Did the dashboard build produce dist/report.html?");
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string InjectBundle(string template, string jsonPayload) {
        int headClose = template.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headClose < 0)
            throw new InvalidOperationException("report.html template missing </head> tag");

        string safePayload = jsonPayload
            .Replace("<", "\\u003C", StringComparison.Ordinal)
            .Replace(">", "\\u003E", StringComparison.Ordinal);

        string inject = $"<script>window.__BUNDLE__ = {safePayload};</script>";
        return string.Concat(template.AsSpan(0, headClose), inject, template.AsSpan(headClose));
    }
}
