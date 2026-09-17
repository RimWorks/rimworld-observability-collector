using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RimWorks.RimObs.Collector.Push;

/// <summary>Grafana's annotations API, enough of it to mark a session as a region. POST opens one
/// with time == timeEnd, PATCH moves timeEnd out when the session ends. Both throw on a non-2xx.</summary>
public sealed class GrafanaAnnotations(HttpClient http) {
    public const string Tag = "rimobs";

    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Returns the new annotation's id.</summary>
    public async Task<long> OpenAsync(string baseUrl, string token, string text, long startMs, CancellationToken cancellationToken) {
        using HttpRequestMessage request = Build(HttpMethod.Post, Endpoint(baseUrl), token, new OpenBody {
            Time = startMs,
            TimeEnd = startMs,
            Text = text,
            Tags = [Tag],
        });

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
        using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument created = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return created.RootElement.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long value) && value > 0
            ? value
            : throw new HttpRequestException("grafana accepted the annotation but returned no id");
    }

    public async Task CloseAsync(string baseUrl, string token, long id, long endMs, CancellationToken cancellationToken) {
        await PatchAsync(baseUrl, token, id, new CloseBody { TimeEnd = endMs }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames an open region. The name usually arrives after the region does.</summary>
    public async Task UpdateTextAsync(string baseUrl, string token, long id, string text, CancellationToken cancellationToken) {
        await PatchAsync(baseUrl, token, id, new TextBody { Text = text }, cancellationToken).ConfigureAwait(false);
    }

    private async Task PatchAsync<T>(string baseUrl, string token, long id, T body, CancellationToken cancellationToken) {
        using HttpRequestMessage request = Build(
            HttpMethod.Patch,
            $"{Endpoint(baseUrl)}/{id.ToString(CultureInfo.InvariantCulture)}",
            token,
            body);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static string Endpoint(string baseUrl) => $"{baseUrl.TrimEnd('/')}/api/annotations";

    private static HttpRequestMessage Build<T>(HttpMethod method, string url, string token, T body) {
        HttpRequestMessage request = new(method, url) { Content = JsonContent.Create(body, options: Json) };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        if (response.IsSuccessStatusCode)
            return;

        string body;
        try {
            body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) {
            body = string.Empty;
        }

        if (body.Length > 500)
            body = body[..500];
        throw new HttpRequestException($"grafana annotations returned {(int)response.StatusCode}{(body.Length == 0 ? string.Empty : $": {body}")}");
    }

    private sealed class OpenBody {
        public long Time { get; set; }

        public long TimeEnd { get; set; }

        public string Text { get; set; } = string.Empty;

        public string[] Tags { get; set; } = [];
    }

    private sealed class CloseBody {
        public long TimeEnd { get; set; }
    }

    private sealed class TextBody {
        public string Text { get; set; } = string.Empty;
    }
}
