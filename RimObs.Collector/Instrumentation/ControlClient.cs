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

    public async Task UnpatchAsync(long id) {
        HttpRequestMessage req = new(HttpMethod.Delete, $"/patch/{id}");
        req.Headers.Add(ControlProtocol.SecretHeader, _secret);
        HttpResponseMessage res = await _http.SendAsync(req);
        if (res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NotFound)
            return;
        throw new ControlClientException((int)res.StatusCode);
    }

    private async Task<T> Roundtrip<T>(HttpMethod method, string path, byte[]? body) where T : class {
        HttpRequestMessage req = new(method, path);
        req.Headers.Add(ControlProtocol.SecretHeader, _secret);
        if (body is not null) {
            req.Content = new ByteArrayContent(body);
        }
        HttpResponseMessage res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) {
            throw new ControlClientException((int)res.StatusCode);
        }
        byte[] raw = await res.Content.ReadAsByteArrayAsync();
        return WireCodec.Deserialize<T>(raw);
    }
}
