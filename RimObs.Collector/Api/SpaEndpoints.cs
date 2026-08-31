using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using RimWorks.RimObs.Collector.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RimWorks.RimObs.Collector.Api;

public static class SpaEndpoints {
    private const string ResourcePrefix = "Dashboard.";
    private static readonly Assembly Host = typeof(SpaEndpoints).Assembly;

    private static readonly Dictionary<string, string> ContentTypes = new() {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
    };

    public static IEndpointRouteBuilder MapSpaEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/", (CollectorToken token) => ServeIndexHtml(token));
        endpoints.MapGet("/assets/{**path}", (string path) => ServeFile("assets/" + path));
        endpoints.MapFallback((HttpContext context, CollectorToken token) => {
            if (context.Request.Path.StartsWithSegments("/api")) {
                return Results.NotFound();
            }

            return ServeIndexHtml(token);
        });

        return endpoints;
    }

    private static IResult ServeIndexHtml(CollectorToken token) {
        Stream? stream = Host.GetManifestResourceStream(ResourcePrefix + "index.html");
        if (stream is null) {
            return Results.NotFound();
        }

        using StreamReader reader = new(stream);
        string html = reader.ReadToEnd();
        return Results.Content(InjectToken(html, token.Value), "text/html; charset=utf-8");
    }

    internal static string InjectToken(string html, string tokenValue) {
        int headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0) {
            return html;
        }

        string script = "<script>window.__RIMOBS_TOKEN__ = " + JsonSerializer.Serialize(tokenValue) + ";</script>";
        return html.Insert(headEnd, script);
    }

    private static IResult ServeFile(string relativePath) {
        Stream? stream = Host.GetManifestResourceStream(ResourcePrefix + relativePath);
        if (stream is null) {
            return Results.NotFound();
        }

        return Results.Stream(stream, ContentTypeFor(relativePath));
    }

    private static string ContentTypeFor(string relativePath) {
        string extension = Path.GetExtension(relativePath);
        return ContentTypes.TryGetValue(extension, out string? type) ? type : "application/octet-stream";
    }
}
