using System.Net;
using System.Text;
using System.Text.Json;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Push;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class MetricsPushServiceTests {
    private static SessionAggregator PopulatedAggregator() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = "sess-1", StopwatchFrequency = 10_000_000 });
        agg.SessionName = "rim colony";
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [5],
            Names = ["Verse.TickManager.DoSingleTick"],
        });
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [5, 5],
            ElapsedTicks = [100L, 300L],
            StartTimestamps = [10L, 400L],
            ParentIds = [-1, -1],
            FrameOrdinals = [1, 2],
            NodeIds = [1, 2],
            ParentNodeIds = [-1, -1],
        });
        agg.OnTpsFps(new TpsFpsBatch { Tps = 58.5, Fps = 61.25, Tick = 42 });
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
        agg.OnVram(new VramBatch { TextureBytes = 100, MeshBytes = 20, RenderTargetBytes = 3 });
        return agg;
    }

    private static MetricsPushOptions EnabledOptions() => new() {
        Enabled = true,
        Endpoint = "http://mimir.example:9009/api/v1/push",
        ExtraLabels = { ["host"] = "desk" },
    };

    private static (MetricsPushService Service, CapturingHandler Handler) Build(SessionAggregator agg) {
        CapturingHandler handler = new();
        return (new MetricsPushService(agg, new ConfigStore(null), log: null, http: new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Disabled_push_sends_nothing() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;

        bool sent = await service.PushOnceAsync(new MetricsPushOptions { Endpoint = "http://x/api/v1/push" }, CancellationToken.None);

        sent.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Enabled_without_an_endpoint_sends_nothing() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;

        bool sent = await service.PushOnceAsync(new MetricsPushOptions { Enabled = true }, CancellationToken.None);

        sent.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sessionless_aggregator_sends_nothing() {
        (MetricsPushService service, CapturingHandler handler) = Build(new SessionAggregator());
        using MetricsPushService _ = service;

        bool sent = await service.PushOnceAsync(EnabledOptions(), CancellationToken.None);

        sent.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Push_posts_a_snappy_remote_write_payload() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.BearerToken = "hunter2";

        bool sent = await service.PushOnceAsync(options, CancellationToken.None);

        sent.Should().BeTrue();
        CapturedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Url.Should().Be("http://mimir.example:9009/api/v1/push");
        request.ContentType.Should().Be("application/x-protobuf");
        request.ContentEncoding.Should().Equal("snappy");
        request.RemoteWriteVersion.Should().Be("0.1.0");
        request.Authorization.Should().Be("Bearer hunter2");
        // snappy block format opens with a varint of the uncompressed length.
        request.Body.Should().NotBeEmpty();
        ReadVarint(request.Body).Should().BeGreaterThan(0);
        // the encoder emits literals only, so series names survive verbatim in the frame.
        Encoding.UTF8.GetString(request.Body).Should().Contain("rimobs_samples_total");
    }

    [Fact]
    public void The_config_document_defaults_to_no_inline_token() {
        new MetricsPushOptions().BearerToken.Should().BeEmpty();
    }

    [Fact]
    public void The_api_masks_the_inline_token_but_disk_keeps_it() {
        RimObsConfig config = new();
        config.MetricsPush.BearerToken = "mimir-secret";

        string onDisk = System.Text.Json.JsonSerializer.Serialize(config, ConfigJson.Options);
        string overHttp = System.Text.Json.JsonSerializer.Serialize(config, ConfigJson.PublicOptions);

        onDisk.Should().Contain("mimir-secret");
        overHttp.Should().NotContain("mimir-secret");
        overHttp.Should().Contain(MetricsPushOptions.RedactedToken);
    }

    [Fact]
    public void An_unset_inline_token_masks_to_nothing() {
        System.Text.Json.JsonSerializer.Serialize(new RimObsConfig(), ConfigJson.PublicOptions)
            .Should().NotContain(MetricsPushOptions.RedactedToken);
    }

    [Fact]
    public async Task Push_authorizes_with_the_configured_token_even_when_the_old_env_var_is_set() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.BearerToken = "from-config";
        Environment.SetEnvironmentVariable("RIMOBS_PUSH_TOKEN", "stale-from-shell-profile");

        try {
            bool sent = await service.PushOnceAsync(options, CancellationToken.None);

            sent.Should().BeTrue();
            handler.Requests.Should().ContainSingle().Which.Authorization.Should().Be("Bearer from-config");
        }
        finally {
            Environment.SetEnvironmentVariable("RIMOBS_PUSH_TOKEN", null);
        }
    }

    [Fact]
    public async Task Push_omits_the_auth_header_when_no_token_is_set() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;

        await service.PushOnceAsync(EnabledOptions(), CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Push_sends_the_tenant_id_as_the_mimir_org_header() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.TenantId = "anonymous";

        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.TenantId.Should().Be("anonymous");
    }

    [Fact]
    public async Task Basic_auth_wins_over_a_bearer_token_and_is_base64_encoded() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.BearerToken = "ignored";
        options.BasicAuth = "12345:glc_token";

        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Authorization
            .Should().Be("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("12345:glc_token")));
    }

    [Fact]
    public void Basic_auth_is_masked_over_http_but_the_tenant_id_is_not() {
        RimObsConfig config = new();
        config.MetricsPush.BasicAuth = "12345:glc_token";
        config.MetricsPush.TenantId = "rimworks";

        string overHttp = System.Text.Json.JsonSerializer.Serialize(config, ConfigJson.PublicOptions);

        overHttp.Should().NotContain("glc_token");
        overHttp.Should().Contain("rimworks");
    }

    [Fact]
    public async Task A_rejected_push_reports_failure_without_throwing() {
        (MetricsPushService service, CapturingHandler handler) = Build(PopulatedAggregator());
        using MetricsPushService _ = service;
        handler.Status = HttpStatusCode.InternalServerError;

        bool sent = await service.PushOnceAsync(EnabledOptions(), CancellationToken.None);

        sent.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_401_logs_the_response_body_so_the_cause_is_readable() {
        CapturingHandler handler = new() {
            Status = HttpStatusCode.Unauthorized,
            ResponseBody = "no org id",
        };
        CapturingLogger log = new();
        using MetricsPushService service = new(PopulatedAggregator(), new ConfigStore(null), log, new HttpClient(handler));

        await service.PushOnceAsync(EnabledOptions(), CancellationToken.None);

        log.Warnings.Should().ContainSingle().Which.Should().Contain("401").And.Contain("no org id");
    }

    [Fact]
    public async Task A_client_timeout_does_not_kill_the_push_loop() {
        CapturingHandler handler = new() { ThrowCanceledFirst = true };
        ConfigStore config = new(null);
        RimObsConfig cfg = new();
        cfg.MetricsPush.Enabled = true;
        cfg.MetricsPush.Endpoint = "http://mimir.example:9009/api/v1/push";
        cfg.MetricsPush.IntervalSeconds = 1;
        config.Replace(cfg);
        using MetricsPushService service = new(PopulatedAggregator(), config, log: null, http: new HttpClient(handler));

        await service.StartAsync(CancellationToken.None);
        try {
            await handler.SecondRequest.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally {
            await service.StopAsync(CancellationToken.None);
        }

        // the first send threw, so anything captured here came from a retry after the timeout.
        handler.Requests.Should().NotBeEmpty();
    }

    [Fact]
    public void Series_carry_the_session_labels_and_the_expected_names() {
        (MetricsPushService service, CapturingHandler _) = Build(PopulatedAggregator());
        using MetricsPushService disposable = service;

        IReadOnlyList<RemoteWriteSample> series = service.BuildSeries(EnabledOptions(), 1_700_000_000_000L);

        series.Select(s => s.MetricName).Should().Contain([
            "rimobs_tps",
            "rimobs_fps",
            "rimobs_tick_ms_p50_session",
            "rimobs_tick_ms_p99_session",
            "rimobs_frame_ms_p50",
            "rimobs_frame_ms_p99",
            "rimobs_alloc_bytes_per_min",
            "rimobs_gc_pause_ms_total",
            "rimobs_gc_events_total",
            "rimobs_vram_tracked_bytes",
            "rimobs_samples_total",
            "rimobs_transport_lost_total",
        ]);
        series.Should().OnlyContain(s => s.TimestampMs == 1_700_000_000_000L);
        series[0].Labels.Should()
            .Contain(new KeyValuePair<string, string>("session_id", "sess-1")).And
            .Contain(new KeyValuePair<string, string>("session_name", "rim colony")).And
            .Contain(new KeyValuePair<string, string>("host", "desk"));
        series.Single(s => s.MetricName == "rimobs_samples_total").Value.Should().Be(2);
        series.Single(s => s.MetricName == "rimobs_gc_pause_ms_total").Value.Should().Be(2.5);
        series.Single(s => s.MetricName == "rimobs_vram_tracked_bytes").Value.Should().Be(123);
    }

    [Fact]
    public void Tick_percentiles_come_from_the_tick_section_not_the_frame_ring() {
        (MetricsPushService service, CapturingHandler _) = Build(PopulatedAggregator());
        using MetricsPushService disposable = service;

        IReadOnlyList<RemoteWriteSample> series = service.BuildSeries(EnabledOptions(), 1L);

        // 10MHz stopwatch, so a 100-tick and a 300-tick sample are 0.01ms and 0.03ms.
        series.Single(s => s.MetricName == "rimobs_tick_ms_p50_session").Value.Should().BeApproximately(0.01, 0.001);
        series.Single(s => s.MetricName == "rimobs_tick_ms_p99_session").Value.Should().BeApproximately(0.03, 0.001);
    }

    [Fact]
    public void A_session_with_no_tick_section_pushes_frame_series_only() {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = "sess-2", StopwatchFrequency = 10_000_000 });
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [9],
            ElapsedTicks = [100L],
            StartTimestamps = [10L],
            ParentIds = [-1],
            FrameOrdinals = [1],
            NodeIds = [1],
            ParentNodeIds = [-1],
        });
        (MetricsPushService service, CapturingHandler _) = Build(agg);
        using MetricsPushService disposable = service;

        IReadOnlyList<RemoteWriteSample> series = service.BuildSeries(EnabledOptions(), 1L);

        series.Should().NotContain(s => s.MetricName.StartsWith("rimobs_tick_ms"));
        series.Select(s => s.MetricName).Should().Contain("rimobs_frame_ms_p99");
    }

    [Fact]
    public void A_new_session_does_not_inherit_the_previous_sessions_readings() {
        SessionAggregator agg = PopulatedAggregator();
        agg.OnSessionMeta(new SessionMeta { SessionId = "sess-2", StopwatchFrequency = 10_000_000 });
        (MetricsPushService service, CapturingHandler _) = Build(agg);
        using MetricsPushService disposable = service;

        IReadOnlyList<RemoteWriteSample> series = service.BuildSeries(EnabledOptions(), 1L);

        series[0].Labels.Should().Contain(new KeyValuePair<string, string>("session_id", "sess-2"));
        series.Select(s => s.MetricName).Should().NotContain([
            "rimobs_tps",
            "rimobs_fps",
            "rimobs_vram_tracked_bytes",
            "rimobs_alloc_bytes_per_min",
        ]);
    }

    // the name rides one info series, never the measurements, so a rename cannot fork them.
    [Fact]
    public void The_name_rides_session_info_and_no_measurement_carries_it() {
        SessionAggregator agg = PopulatedAggregator();
        agg.SessionName = string.Empty;
        (MetricsPushService service, CapturingHandler _) = Build(agg);
        using MetricsPushService disposable = service;

        IReadOnlyList<RemoteWriteSample> series = service.BuildSeries(EnabledOptions(), 1L);

        RemoteWriteSample info = series.Single(s => s.MetricName == "rimobs_session_info");
        info.Value.Should().Be(1);
        info.Labels.Should().Contain(new KeyValuePair<string, string>("session_name", "sess-1"));
        series.Where(s => s.MetricName != "rimobs_session_info")
            .SelectMany(s => s.Labels).Should().NotContain(l => l.Key == "session_name");
    }

    // tertius: a changed label VALUE is a new series in prometheus, so renaming a session
    // mid-run used to fork every metric and restart every rate().
    [Fact]
    public void Renaming_a_session_leaves_every_measurement_label_untouched() {
        SessionAggregator agg = PopulatedAggregator();
        agg.SessionName = string.Empty;
        (MetricsPushService service, CapturingHandler _) = Build(agg);
        using MetricsPushService disposable = service;

        static List<KeyValuePair<string, string>> Measured(IReadOnlyList<RemoteWriteSample> s) =>
            s.Where(x => x.MetricName != "rimobs_session_info")
                .SelectMany(x => x.Labels).Distinct().OrderBy(l => l.Key).ToList();

        List<KeyValuePair<string, string>> unnamed = Measured(service.BuildSeries(EnabledOptions(), 1L));
        agg.SessionName = "rim colony";
        List<KeyValuePair<string, string>> named = Measured(service.BuildSeries(EnabledOptions(), 2L));

        named.Should().BeEquivalentTo(unnamed);
        named.Should().NotContain(l => l.Key == "session_name");
    }

    [Fact]
    public void Extra_labels_that_collide_with_the_session_labels_are_dropped() {
        (MetricsPushService service, CapturingHandler _) = Build(PopulatedAggregator());
        using MetricsPushService disposable = service;
        MetricsPushOptions options = EnabledOptions();
        options.ExtraLabels["session_id"] = "spoofed";
        options.ExtraLabels["session_name"] = "spoofed";

        IReadOnlyList<KeyValuePair<string, string>> labels = service.BuildSeries(options, 1L)[0].Labels;

        labels.Where(l => l.Key == "session_id").Should().ContainSingle().Which.Value.Should().Be("sess-1");
        labels.Where(l => l.Key == "session_name").Should().ContainSingle().Which.Value.Should().Be("rim colony");
        labels.Should().Contain(new KeyValuePair<string, string>("host", "desk"));
    }

    [Fact]
    public void Extra_labels_with_illegal_names_are_dropped() {
        (MetricsPushService service, CapturingHandler _) = Build(PopulatedAggregator());
        using MetricsPushService disposable = service;
        MetricsPushOptions options = EnabledOptions();
        options.ExtraLabels["has-dash"] = "x";
        options.ExtraLabels["9leading"] = "x";
        options.ExtraLabels["has space"] = "x";
        options.ExtraLabels[string.Empty] = "x";

        IReadOnlyList<KeyValuePair<string, string>> labels = service.BuildSeries(options, 1L)[0].Labels;

        labels.Select(l => l.Key).Should().BeEquivalentTo("session_id", "session_name", "host");
    }

    [Theory]
    [InlineData("host", true)]
    [InlineData("_private", true)]
    [InlineData("A9_z", true)]
    [InlineData("has-dash", false)]
    [InlineData("9leading", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void Label_names_follow_the_prometheus_rule(string name, bool valid) =>
        MetricsPushService.IsValidLabelName(name).Should().Be(valid);

    [Fact]
    public async Task The_annotation_opens_at_the_session_start_not_the_push_tick() {
        SessionAggregator agg = new();
        DateTime started = DateTime.UtcNow.AddMinutes(-20);
        agg.OnSessionMeta(new SessionMeta {
            SessionId = "sess-1",
            StopwatchFrequency = 10_000_000,
            StartedUtcTicks = started.Ticks,
        });
        (MetricsPushService service, CapturingHandler handler) = Build(agg);
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.GrafanaUrl = "http://grafana.example:3000";

        await service.PushOnceAsync(options, CancellationToken.None);

        CapturedRequest annotation = handler.Requests.Single(r => r.Url.EndsWith("/api/annotations", StringComparison.Ordinal));
        using JsonDocument body = JsonDocument.Parse(annotation.Body);
        long expected = new DateTimeOffset(started, TimeSpan.Zero).ToUnixTimeMilliseconds();
        body.RootElement.GetProperty("time").GetInt64().Should().Be(expected);
        body.RootElement.GetProperty("timeEnd").GetInt64().Should().Be(expected);
    }

    [Fact]
    public async Task A_game_that_went_quiet_closes_the_region_at_its_last_batch() {
        SessionAggregator agg = PopulatedAggregator();
        agg.OnBatchReceived(64);
        (MetricsPushService service, CapturingHandler handler) = Build(agg);
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.GrafanaUrl = "http://grafana.example:3000";
        handler.ResponseBody = """{"id":7}""";

        await service.PushOnceAsync(options, CancellationToken.None);
        // negative, so the batch above counts as stale no matter how coarse the clock is.
        service.IdleTimeout = TimeSpan.FromMilliseconds(-1);
        await service.PushOnceAsync(options, CancellationToken.None);

        CapturedRequest close = handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch).Subject;
        close.Url.Should().EndWith("/api/annotations/7");
        using JsonDocument body = JsonDocument.Parse(close.Body);
        body.RootElement.GetProperty("timeEnd").GetInt64()
            .Should().Be(new DateTimeOffset(agg.LastBatchUtc, TimeSpan.Zero).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task A_failed_open_retries_on_the_next_tick() {
        SessionAggregator agg = PopulatedAggregator();
        agg.OnBatchReceived(64);
        (MetricsPushService service, CapturingHandler handler) = Build(agg);
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.GrafanaUrl = "http://grafana.example:3000";
        handler.ResponseBody = """{"id":7}""";
        handler.FailAnnotationsFirst = 1;

        await service.PushOnceAsync(options, CancellationToken.None);
        await service.PushOnceAsync(options, CancellationToken.None);
        // the region is claimed now, so a rename proves the second open landed and stuck.
        service.IdleTimeout = TimeSpan.FromMilliseconds(-1);
        await service.PushOnceAsync(options, CancellationToken.None);

        handler.Requests.Where(r => r.Url.EndsWith("/api/annotations", StringComparison.Ordinal)).Should().HaveCount(2);
        handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch)
            .Which.Url.Should().EndWith("/api/annotations/7");
    }

    [Fact]
    public async Task A_dead_remote_write_endpoint_still_syncs_the_annotation() {
        SessionAggregator agg = PopulatedAggregator();
        (MetricsPushService service, CapturingHandler handler) = Build(agg);
        using MetricsPushService _ = service;
        MetricsPushOptions options = EnabledOptions();
        options.GrafanaUrl = "http://grafana.example:3000";
        handler.ResponseBody = """{"id":7}""";
        handler.ThrowForRemoteWrite = true;

        bool sent = await service.PushOnceAsync(options, CancellationToken.None);

        sent.Should().BeFalse();
        handler.Requests.Should().ContainSingle(r => r.Url.EndsWith("/api/annotations", StringComparison.Ordinal));
    }

    private static long ReadVarint(byte[] bytes) {
        long value = 0;
        int shift = 0;
        foreach (byte b in bytes) {
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
            shift += 7;
        }
        return -1;
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Url,
        string? ContentType,
        string[] ContentEncoding,
        string? RemoteWriteVersion,
        string? TenantId,
        string? Authorization,
        byte[] Body);

    private sealed class CapturingHandler : HttpMessageHandler {
        private int _sent;

        public List<CapturedRequest> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;
        public string ResponseBody { get; set; } = string.Empty;

        // grafana restarting under us: the first N annotation calls 502, the rest are healthy.
        public int FailAnnotationsFirst { get; set; }

        // mimir refusing the connection: remote write throws, annotations stay healthy.
        public bool ThrowForRemoteWrite { get; set; }

        // stands in for HttpClient's own timeout, which surfaces as a cancellation the loop must survive.
        public bool ThrowCanceledFirst { get; set; }

        public TaskCompletionSource SecondRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (Interlocked.Increment(ref _sent) == 2)
                SecondRequest.TrySetResult();
            if (ThrowCanceledFirst && _sent == 1)
                throw new TaskCanceledException("simulated HttpClient timeout");
            if (ThrowForRemoteWrite && request.RequestUri?.AbsolutePath == "/api/v1/push")
                throw new HttpRequestException("connection refused");

            byte[] body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Content?.Headers.ContentType?.MediaType,
                [.. request.Content?.Headers.ContentEncoding ?? []],
                request.Headers.TryGetValues("X-Prometheus-Remote-Write-Version", out IEnumerable<string>? v) ? v.First() : null,
                request.Headers.TryGetValues("X-Scope-OrgID", out IEnumerable<string>? t) ? t.First() : null,
                request.Headers.Authorization?.ToString(),
                body));
            if (FailAnnotationsFirst > 0 && request.RequestUri?.AbsolutePath.StartsWith("/api/annotations", StringComparison.Ordinal) == true) {
                FailAnnotationsFirst--;
                return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("restarting") };
            }

            return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody) };
        }
    }

    private sealed class CapturingLogger : ILogger<MetricsPushService> {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (logLevel == LogLevel.Warning)
                Warnings.Add($"{formatter(state, exception)} {exception?.Message}");
        }
    }
}
