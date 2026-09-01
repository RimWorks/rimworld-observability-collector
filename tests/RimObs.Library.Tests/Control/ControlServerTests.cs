using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Tests;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using RimObsTest.Fixtures;
using Xunit;

namespace RimWorks.RimObs.Library.Tests.Control;

public class ControlServerTests : IDisposable {
    private readonly ControlServer _server;
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _drainCts;
    private readonly Task _drainTask;

    public ControlServerTests() {
        TestBackend.Activate();
        PatchInstaller.ResetForTests();
        PatchRegistry.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        ControlServices.ResetForTests();

        _server = new ControlServer(
            secret: "topsecret",
            frameworkPackageId: "test.pkg",
            assemblies: () => new[] { typeof(ResolverTargets).Assembly });
        _server.Start();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}/") };

        _drainCts = new CancellationTokenSource();
        _drainTask = Task.Run(() => {
            while (!_drainCts.Token.IsCancellationRequested) {
                ControlServices.Queue.Drain();
                Thread.Sleep(5);
            }
        });
    }

    public void Dispose() {
        _drainCts.Cancel();
        try { _drainTask.Wait(TimeSpan.FromSeconds(1)); }
        catch { /* swallow cancellation */ }
        _drainCts.Dispose();

        _client.Dispose();
        _server.Stop();

        PatchRegistry.ResetForTests();
        PatchInstaller.ResetForTests();
        TestBackend.Deactivate();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        ControlServices.ResetForTests();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Rejects_request_without_secret_header() {
        HttpResponseMessage res = await _client.PostAsync("/search",
            new ByteArrayContent([0]));
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rejects_request_with_wrong_secret() {
        HttpRequestMessage req = new(HttpMethod.Post, "/search") {
            Content = new ByteArrayContent([0]),
        };
        req.Headers.Add("X-RimObs-Control", "wrong");
        HttpResponseMessage res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Returns_404_for_unknown_path_with_valid_secret() {
        HttpRequestMessage req = new(HttpMethod.Get, "/nope");
        req.Headers.Add("X-RimObs-Control", "topsecret");
        HttpResponseMessage res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_returns_results_for_substring_match() {
        ControlSearchRequest req = new() { Query = "ResolverTargets", Limit = 5 };
        HttpResponseMessage res = await PostMsg("/search", WireCodec.Serialize(req));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        byte[] body = await res.Content.ReadAsByteArrayAsync();
        ControlSearchResponse decoded = WireCodec.Deserialize<ControlSearchResponse>(body);
        decoded.Results.Should().NotBeEmpty();
        decoded.Results.Should().Contain(r => r.MethodName == "Add");
    }

    [Fact]
    public async Task Patch_applies_and_list_returns_it() {
        ControlPatchRequest req = new() {
            TypeFullName = typeof(ResolverTargets).FullName!,
            MethodName = "Add",
            ParamTypeFullNames = [typeof(int).FullName!, typeof(int).FullName!],
        };

        HttpResponseMessage res = await PostMsg("/patch", WireCodec.Serialize(req));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // The POST only enqueues the patch op; the background drain pump applies it ~5ms later.
        // Poll /patches until the apply lands instead of reading once and racing the drain.
        ControlPatchListResponse decoded = new();
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline) {
            HttpResponseMessage list = await GetMsg("/patches");
            decoded = WireCodec.Deserialize<ControlPatchListResponse>(
                await list.Content.ReadAsByteArrayAsync());
            if (decoded.Patches.Length > 0)
                break;
            await Task.Delay(10);
        }

        decoded.Patches.Should().NotBeEmpty();
    }

    private async Task<HttpResponseMessage> PostMsg(string path, byte[] body) {
        HttpRequestMessage req = new(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        req.Headers.Add("X-RimObs-Control", "topsecret");
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> GetMsg(string path) {
        HttpRequestMessage req = new(HttpMethod.Get, path);
        req.Headers.Add("X-RimObs-Control", "topsecret");
        return await _client.SendAsync(req);
    }
}
