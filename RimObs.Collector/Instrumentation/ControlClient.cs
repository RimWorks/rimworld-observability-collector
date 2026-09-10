using System.Net;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;

namespace RimWorks.RimObs.Collector.Instrumentation;

public sealed class ControlClient {
    private readonly string _secret;
    private readonly HttpClient _http;

    public ControlClient(int port, string secret) {
        _secret = secret;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
    }

    public async Task<ControlSearchResponse> SearchAsync(ControlSearchRequest req) =>
        await Roundtrip<ControlSearchResponse>(HttpMethod.Post, "/search", WireCodec.Serialize(req));

    public async Task<ControlPatchResponse> PatchAsync(ControlPatchRequest req) =>
        await Roundtrip<ControlPatchResponse>(HttpMethod.Post, "/patch", WireCodec.Serialize(req));

    public async Task<ControlPatchListResponse> ListAsync() =>
        await Roundtrip<ControlPatchListResponse>(HttpMethod.Get, "/patches", null);

    /// <summary>Asks the game to restart itself, optionally saving the colony first.</summary>
    public async Task RestartGameAsync(bool save) {
        HttpRequestMessage req = new(HttpMethod.Post, $"/session/restart-game?save={(save ? "true" : "false")}");
        req.Headers.Add(ControlProtocol.SecretHeader, _secret);
        HttpResponseMessage res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            throw await Failure(res);
    }

    /// <summary>Tells the game to re-anchor onto a new session id.</summary>
    public async Task NewSessionAsync() {
        HttpRequestMessage req = new(HttpMethod.Post, "/session/new");
        req.Headers.Add(ControlProtocol.SecretHeader, _secret);
        HttpResponseMessage res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            throw await Failure(res);
    }

    public async Task UnpatchAsync(long id) {
        HttpRequestMessage req = new(HttpMethod.Delete, $"/patch/{id}");
        req.Headers.Add(ControlProtocol.SecretHeader, _secret);
        HttpResponseMessage res = await _http.SendAsync(req);
        if (res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NotFound)
            return;
        throw await Failure(res);
    }

    private async Task<T> Roundtrip<T>(HttpMethod method, string path, byte[]? body) where T : class {
        HttpRequestMessage req = new(method, path);
        req.Headers.Add(ControlProtocol.SecretHeader, _secret);
        if (body is not null) {
            req.Content = new ByteArrayContent(body);
        }
        HttpResponseMessage res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) {
            throw await Failure(res);
        }
        byte[] raw = await res.Content.ReadAsByteArrayAsync();
        return WireCodec.Deserialize<T>(raw);
    }

    // the library answers a refusal or a drain timeout with a ControlPatchResponse body, so keep
    // its reason instead of throwing away everything but the status code.
    private static async Task<ControlClientException> Failure(HttpResponseMessage res) {
        byte[] raw = await res.Content.ReadAsByteArrayAsync();
        string? reason = null;
        if (raw.Length > 0) {
            try { reason = WireCodec.Deserialize<ControlPatchResponse>(raw).ErrorReason; }
            catch (Exception) { reason = null; }
        }
        return new ControlClientException((int)res.StatusCode, string.IsNullOrEmpty(reason) ? null : reason);
    }
}
