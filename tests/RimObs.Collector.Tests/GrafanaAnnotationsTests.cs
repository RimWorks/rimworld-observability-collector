using System.Net;
using System.Text;
using System.Text.Json;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Push;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class GrafanaAnnotationsTests {
    private const string GrafanaUrl = "http://grafana.example:3000";

    private static SessionAggregator Aggregator(string sessionId, string name) {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = sessionId, StopwatchFrequency = 10_000_000 });
        agg.SessionName = name;
        agg.OnGcEvents(new GcEventsBatch {
            Generations = [0],
            PauseTypes = [0],
            HeapBefore = [1000L],
            HeapAfter = [500L],
            DurationMicros = [2500L],
            Ticks = [1L],
            AllocationRateBytesPerMinute = [777L],
            FrameOrdinals = [1],
        });
        return agg;
    }

    private static ConfigStore Store(MetricsPushOptions push) {
        RimObsConfig config = new() { MetricsPush = push };
        ConfigStore store = new(null);
        store.Replace(config);
        return store;
    }

    private static MetricsPushOptions Options(bool withEndpoint = false) => new() {
        Enabled = true,
        Endpoint = withEndpoint ? "http://mimir.example:9009/api/v1/push" : string.Empty,
        GrafanaUrl = GrafanaUrl,
        GrafanaToken = "graf-token",
    };

    [Fact]
    public async Task The_first_session_opens_a_rimobs_tagged_region() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);

        Sent sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Url.Should().Be($"{GrafanaUrl}/api/annotations");
        sent.Authorization.Should().Be("Bearer graf-token");
        JsonElement body = JsonDocument.Parse(sent.Body).RootElement;
        body.GetProperty("text").GetString().Should().Be("rim colony");
        body.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal(GrafanaAnnotations.Tag);
        long time = body.GetProperty("time").GetInt64();
        time.Should().BeGreaterThan(0);
        body.GetProperty("timeEnd").GetInt64().Should().Be(time);
    }

    [Fact]
    public async Task An_unnamed_session_is_annotated_with_its_id() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", string.Empty), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);

        JsonDocument.Parse(handler.Sent[0].Body).RootElement.GetProperty("text").GetString().Should().Be("sess-1");
    }

    [Fact]
    public async Task A_repeat_tick_on_the_same_session_annotates_nothing_new() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task A_new_session_closes_the_old_region_then_opens_a_new_one() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        SessionAggregator agg = Aggregator("sess-1", "rim colony");
        using MetricsPushService service = new(agg, Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        agg.OnSessionMeta(new SessionMeta { SessionId = "sess-2", StopwatchFrequency = 10_000_000 });
        agg.SessionName = "second run";
        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Sent.Select(s => (s.Method.Method, s.Url)).Should().Equal(
            ("POST", $"{GrafanaUrl}/api/annotations"),
            ("PATCH", $"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}"),
            ("POST", $"{GrafanaUrl}/api/annotations"));
        JsonDocument.Parse(handler.Sent[1].Body).RootElement.GetProperty("timeEnd").GetInt64().Should().BeGreaterThan(0);
        JsonDocument.Parse(handler.Sent[2].Body).RootElement.GetProperty("text").GetString().Should().Be("second run");
    }

    [Fact]
    public async Task A_name_typed_after_the_region_opened_renames_it() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        SessionAggregator agg = Aggregator("sess-1", string.Empty);
        using MetricsPushService service = new(agg, Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        agg.SessionName = "rim colony";
        await service.PushOnceAsync(options, CancellationToken.None);
        // the rename already landed, so a third tick must not repeat it.
        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Sent.Select(s => (s.Method.Method, s.Url)).Should().Equal(
            ("POST", $"{GrafanaUrl}/api/annotations"),
            ("PATCH", $"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}"));
        JsonElement patched = JsonDocument.Parse(handler.Sent[1].Body).RootElement;
        patched.GetProperty("text").GetString().Should().Be("rim colony");
        patched.TryGetProperty("timeEnd", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Shutdown_closes_the_open_region() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        handler.Sent.Select(s => s.Method.Method).Should().Equal("POST", "PATCH");
        handler.Sent[1].Url.Should().Be($"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}");

        // nothing is open now, so a second stop must not fire another close.
        await service.StopAsync(CancellationToken.None);
        handler.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task No_grafana_url_means_no_annotation_calls() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options(withEndpoint: true);
        options.GrafanaUrl = string.Empty;
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        handler.Sent.Should().OnlyContain(s => s.Url == "http://mimir.example:9009/api/v1/push");
    }

    [Fact]
    public async Task A_disabled_push_block_annotates_nothing() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options(withEndpoint: true);
        options.Enabled = false;
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        handler.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Turning_push_off_mid_session_closes_the_region_and_re_enabling_opens_a_new_one() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        options.Enabled = false;
        await service.PushOnceAsync(options, CancellationToken.None);
        await service.PushOnceAsync(options, CancellationToken.None);
        options.Enabled = true;
        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Sent.Select(s => (s.Method.Method, s.Url)).Should().Equal(
            ("POST", $"{GrafanaUrl}/api/annotations"),
            ("PATCH", $"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}"),
            ("POST", $"{GrafanaUrl}/api/annotations"));
        JsonDocument.Parse(handler.Sent[1].Body).RootElement.GetProperty("timeEnd").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Clearing_the_grafana_url_mid_session_still_closes_against_the_old_url() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        options.GrafanaUrl = string.Empty;
        await service.PushOnceAsync(options, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        handler.Sent.Select(s => (s.Method.Method, s.Url)).Should().Equal(
            ("POST", $"{GrafanaUrl}/api/annotations"),
            ("PATCH", $"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}"));
    }

    [Fact]
    public async Task A_failed_open_is_retried_next_tick_and_the_pushes_keep_going() {
        FakeGrafana handler = new() { FailOpen = true };
        MetricsPushOptions options = Options(withEndpoint: true);
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        bool first = await service.PushOnceAsync(options, CancellationToken.None);
        bool second = await service.PushOnceAsync(options, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeTrue();
        handler.Sent.Count(s => s.Url.EndsWith("/api/v1/push", StringComparison.Ordinal)).Should().Be(2);
        // one open attempt per tick, and no close for an annotation that never opened.
        handler.Sent.Where(s => s.Url.Contains("/api/annotations", StringComparison.Ordinal))
            .Should().OnlyContain(s => s.Method == HttpMethod.Post).And.HaveCount(2);
    }

    [Fact]
    public async Task A_session_that_failed_to_open_gets_its_region_once_grafana_comes_back() {
        FakeGrafana handler = new() { FailOpen = true };
        MetricsPushOptions options = Options();
        using MetricsPushService service = new(Aggregator("sess-1", "rim colony"), Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        handler.FailOpen = false;
        await service.PushOnceAsync(options, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        handler.Sent.Select(s => (s.Method.Method, s.Url)).Should().Equal(
            ("POST", $"{GrafanaUrl}/api/annotations"),
            ("POST", $"{GrafanaUrl}/api/annotations"),
            ("PATCH", $"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}"));
    }

    // tertius: a close that failed was forgotten, so the region stayed a point forever
    // and ka asked for annotations that mark both a start AND an end.
    [Fact]
    public async Task A_close_that_fails_is_retried_on_the_next_tick() {
        FakeGrafana handler = new();
        MetricsPushOptions options = Options();
        SessionAggregator agg = Aggregator("sess-1", "rim colony");
        using MetricsPushService service = new(agg, Store(options), log: null, http: new HttpClient(handler));

        await service.PushOnceAsync(options, CancellationToken.None);
        handler.Sent.Should().ContainSingle();

        handler.FailClose = true;
        agg.OnSessionMeta(new SessionMeta { SessionId = "sess-2" });
        await service.PushOnceAsync(options, CancellationToken.None);
        handler.Sent.Where(x => x.Method == HttpMethod.Patch).Should().ContainSingle();

        handler.FailClose = false;
        await service.PushOnceAsync(options, CancellationToken.None);

        IEnumerable<Sent> closes = handler.Sent.Where(x => x.Method == HttpMethod.Patch);
        closes.Should().HaveCount(2, "the failed close is re-sent until grafana takes it");
        closes.Should().OnlyContain(x => x.Url == $"{GrafanaUrl}/api/annotations/{FakeGrafana.FirstId}");
    }

    private sealed record Sent(HttpMethod Method, string Url, string? Authorization, string Body);

    private sealed class FakeGrafana : HttpMessageHandler {
        public const long FirstId = 42;

        private long _nextId = FirstId;

        public List<Sent> Sent { get; } = [];

        public bool FailOpen { get; set; }

        /// <summary>Fails every close until cleared, so a retry is observable.</summary>
        public bool FailClose { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Sent.Add(new Sent(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));

            bool opening = request.Method == HttpMethod.Post
                && request.RequestUri.AbsolutePath.EndsWith("/api/annotations", StringComparison.Ordinal);
            if (!opening) {
                return FailClose
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (FailOpen)
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("no annotation scope") };

            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent($"{{\"id\":{_nextId++},\"message\":\"Annotation added\"}}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
