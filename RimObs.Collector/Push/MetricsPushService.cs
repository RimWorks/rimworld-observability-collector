using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RimWorks.RimObs.Collector.Push;

/// <summary>
/// Pushes session-level series to a prometheus remote-write endpoint. Off unless the config says
/// otherwise, and the config is re-read every tick so the dashboard toggle lands without a restart.
/// </summary>
public sealed class MetricsPushService : BackgroundService {
    private readonly SessionAggregator _aggregator;
    private readonly ConfigStore _config;
    private readonly ILogger<MetricsPushService>? _log;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly GrafanaAnnotations _annotations;
    private readonly ProfilePush _profiles;
    private int _consecutiveFailures;
    private int _loggedBadLabels;
    private string? _annotatedSessionId;
    private string? _annotationUrl;
    private string? _annotationToken;
    private string? _annotationText;
    private long _annotationId;
    private bool _annotationFailing;

    /// <summary>A close that did not land. Retried before the next open, or the region stays a point.</summary>
    private (string Url, string Token, long Id, long EndMs)? _pendingClose;

    /// <summary>Silence this long means the game is gone, the same signal the parent watcher idles on.
    /// Settable for tests.</summary>
    internal TimeSpan IdleTimeout { get; set; } = ParentProcessWatcher.IdleTimeout;

    public MetricsPushService(
        SessionAggregator aggregator,
        ConfigStore config,
        ILogger<MetricsPushService>? log = null,
        HttpClient? http = null
    ) {
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(config);
        _aggregator = aggregator;
        _config = config;
        _log = log;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _annotations = new GrafanaAnnotations(_http);
        _profiles = new ProfilePush(_http);
    }

    public override void Dispose() {
        base.Dispose();
        if (_ownsHttp)
            _http.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            MetricsPushOptions options = _config.Current.MetricsPush;
            try {
                await PushOnceAsync(options, stoppingToken).ConfigureAwait(false);
            }
            // HttpClient's own timeout throws TaskCanceledException too; only our token ends the loop.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                OnFailure(options.Endpoint, ex);
            }

            try {
                await Task.Delay(
                    TimeSpan.FromSeconds(MetricsPushOptions.ClampIntervalSeconds(options.IntervalSeconds)),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!cancellationToken.IsCancellationRequested)
            await SyncAnnotationAsync(_config.Current.MetricsPush, sessionId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns true when a payload actually went out. The annotation runs after the push
    /// and swallows its own failures, so grafana being down never costs a metrics tick.</summary>
    internal async Task<bool> PushOnceAsync(MetricsPushOptions options, CancellationToken cancellationToken) {
        bool sent = false;
        try {
            sent = await PushSeriesAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            // mimir being unreachable must not skip the annotation, or an open region never closes.
            OnFailure(options.Endpoint, ex);
        }

        await SyncAnnotationAsync(options, _aggregator.Meta?.SessionId, cancellationToken).ConfigureAwait(false);
        await PushProfileAsync(options, cancellationToken).ConfigureAwait(false);
        return sent;
    }

    /// <summary>Sends the call tree to pyroscope. Its failures are swallowed the same way the
    /// annotations' are, so a profile store being down never costs a metrics push.</summary>
    private async Task PushProfileAsync(MetricsPushOptions options, CancellationToken cancellationToken) {
        if (!options.Enabled)
            return;

        try {
            await _profiles.PushAsync(_aggregator, options, _aggregator.Meta?.SessionId ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            OnFailure(options.ProfileEndpoint, ex);
        }
    }

    /// <summary>Opens an annotation for a new session and closes the one it replaces. A null
    /// <paramref name="sessionId"/>, push going off, or a quiet game closes and opens nothing.</summary>
    private async Task SyncAnnotationAsync(MetricsPushOptions options, string? sessionId, CancellationToken cancellationToken) {
        await RetryPendingCloseAsync(cancellationToken).ConfigureAwait(false);
        // enabled is the master switch for the whole metrics_push block, drawer included.
        bool off = !options.Enabled || string.IsNullOrWhiteSpace(options.GrafanaUrl);
        // standalone serve has no parent pid to watch, so silence is the only disconnect signal.
        DateTime lastBatch = _aggregator.LastBatchUtc;
        bool idle = lastBatch != default && DateTime.UtcNow - lastBatch > IdleTimeout;
        bool closing = off || idle || string.IsNullOrWhiteSpace(sessionId);
        if (closing) {
            if (_annotationId <= 0 && _annotatedSessionId is null)
                return;
        }
        else if (sessionId == _annotatedSessionId) {
            await RenameAnnotationAsync(sessionId!, cancellationToken).ConfigureAwait(false);
            // heartbeat the end: a killed collector never reaches StopAsync, so without this
            // the region keeps the start as its end and the session reads as a point.
            await HeartbeatAnnotationAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // a region that ended with the game should end at the last thing the game said.
        long endMs = idle
            ? new DateTimeOffset(new DateTime(lastBatch.Ticks, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : nowMs;
        string logUrl = off ? _annotationUrl ?? string.Empty : options.GrafanaUrl;
        bool called = false;
        try {
            // the close goes to whatever opened it, so a toggled-off url still lands.
            if (_annotationId > 0) {
                await _annotations.CloseAsync(_annotationUrl!, _annotationToken ?? string.Empty, _annotationId, endMs, cancellationToken).ConfigureAwait(false);
                called = true;
            }

            _annotationId = 0;
            _annotatedSessionId = null;
            _annotationUrl = null;
            _annotationToken = null;
            _annotationText = null;
            if (!closing) {
                called = true;
                string token = options.GrafanaToken ?? string.Empty;
                string text = AnnotationText(sessionId!);
                _annotationId = await _annotations.OpenAsync(options.GrafanaUrl, token, text, SessionStartMs(nowMs), cancellationToken).ConfigureAwait(false);
                _annotatedSessionId = sessionId;
                _annotationUrl = options.GrafanaUrl;
                _annotationToken = token;
                _annotationText = text;
            }

            if (called && _annotationFailing) {
                _annotationFailing = false;
                _log?.LogInformation("Grafana annotations at {Url} recovered", logUrl);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            // a close that never landed leaves the region open-ended, so hold it for a retry.
            if (_annotationId > 0 && _annotationUrl is not null)
                _pendingClose = (_annotationUrl, _annotationToken ?? string.Empty, _annotationId, endMs);
            // claim nothing else, so the next tick re-opens; _annotationFailing keeps the log quiet.
            _annotationId = 0;
            _annotationUrl = null;
            _annotationToken = null;
            _annotationText = null;
            _annotatedSessionId = null;
            if (!_annotationFailing) {
                _annotationFailing = true;
                _log?.LogWarning(ex, "Grafana annotation at {Url} failed; further failures stay quiet", logUrl);
            }
        }
    }

    /// <summary>Walks the region's end forward to now, so a hard kill leaves it ending within
    /// one interval of the truth instead of never ending at all.</summary>
    private async Task HeartbeatAnnotationAsync(CancellationToken cancellationToken) {
        if (_annotationId <= 0 || _annotationUrl is null)
            return;

        try {
            await _annotations.CloseAsync(
                _annotationUrl,
                _annotationToken ?? string.Empty,
                _annotationId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception) {
            // the real close still retries through _pendingClose; a missed beat costs one interval.
        }
    }

    /// <summary>Re-sends a close that failed earlier. Dropped only once grafana accepts it.</summary>
    private async Task RetryPendingCloseAsync(CancellationToken cancellationToken) {
        if (_pendingClose is not { } pending)
            return;

        try {
            await _annotations.CloseAsync(pending.Url, pending.Token, pending.Id, pending.EndMs, cancellationToken).ConfigureAwait(false);
            _pendingClose = null;
            if (_annotationFailing) {
                _annotationFailing = false;
                _log?.LogInformation("Grafana annotations at {Url} recovered", pending.Url);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception) {
            // still down; the next tick tries again and _annotationFailing keeps the log quiet.
        }
    }

    /// <summary>The dashboard names a session after it starts, so the open region gets renamed in
    /// place. A failed rename sticks the text anyway; retrying every tick is the spam that avoids.</summary>
    private async Task RenameAnnotationAsync(string sessionId, CancellationToken cancellationToken) {
        if (_annotationId <= 0)
            return;

        string text = AnnotationText(sessionId);
        if (text == _annotationText)
            return;

        try {
            await _annotations.UpdateTextAsync(_annotationUrl!, _annotationToken ?? string.Empty, _annotationId, text, cancellationToken).ConfigureAwait(false);
            _annotationText = text;
            if (_annotationFailing) {
                _annotationFailing = false;
                _log?.LogInformation("Grafana annotations at {Url} recovered", _annotationUrl);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            _annotationText = text;
            if (!_annotationFailing) {
                _annotationFailing = true;
                _log?.LogWarning(ex, "Grafana annotation at {Url} failed; further failures stay quiet", _annotationUrl);
            }
        }
    }

    private string AnnotationText(string sessionId) =>
        string.IsNullOrWhiteSpace(_aggregator.SessionName) ? sessionId : _aggregator.SessionName;

    /// <summary>The region opens when the session started, not when push was toggled on.</summary>
    private long SessionStartMs(long fallbackMs) {
        long ticks = _aggregator.Meta?.StartedUtcTicks ?? 0;
        return ticks > 0
            ? new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : fallbackMs;
    }

    private async Task<bool> PushSeriesAsync(MetricsPushOptions options, CancellationToken cancellationToken) {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Endpoint))
            return false;

        IReadOnlyList<RemoteWriteSample> series = BuildSeries(options, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (series.Count == 0)
            return false;

        byte[] payload = RemoteWriteEncoder.Encode(series);
        using ByteArrayContent content = new(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        content.Headers.ContentEncoding.Add("snappy");
        using HttpRequestMessage request = new(HttpMethod.Post, options.Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Prometheus-Remote-Write-Version", "0.1.0");
        if (!string.IsNullOrWhiteSpace(options.TenantId))
            request.Headers.TryAddWithoutValidation("X-Scope-OrgID", options.TenantId);
        if (!string.IsNullOrWhiteSpace(options.BasicAuth))
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.BasicAuth)));
        else if (!string.IsNullOrWhiteSpace(options.BearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            // a 401 says nothing on its own; mimir puts the reason in the body.
            string detail = await ReadFailureDetailAsync(response, cancellationToken).ConfigureAwait(false);
            OnFailure(options.Endpoint, new HttpRequestException($"remote write returned {(int)response.StatusCode}{detail}"));
            return false;
        }

        if (Interlocked.Exchange(ref _consecutiveFailures, 0) > 0)
            _log?.LogInformation("Metrics push to {Endpoint} recovered", options.Endpoint);
        return true;
    }

    private static async Task<string> ReadFailureDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        try {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            body = body.Trim();
            if (body.Length == 0)
                return string.Empty;

            return $": {(body.Length > 500 ? body[..500] : body)}";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) {
            return string.Empty;
        }
    }

    private void OnFailure(string endpoint, Exception ex) {
        // only the first of a run is worth a warning; a dead endpoint at 0.1hz is still spam.
        if (Interlocked.Increment(ref _consecutiveFailures) == 1)
            _log?.LogWarning(ex, "Metrics push to {Endpoint} failed; further failures stay quiet", endpoint);
    }

    internal IReadOnlyList<RemoteWriteSample> BuildSeries(MetricsPushOptions options, long nowMs) {
        string? sessionId = _aggregator.Meta?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        // session_id only: in prometheus a changed label VALUE is a new series, so carrying
        // the name here forks every metric the moment a session is renamed. the name rides
        // rimobs_session_info instead, which the dashboard joins on.
        List<KeyValuePair<string, string>> labels = [new("session_id", sessionId)];
        foreach (KeyValuePair<string, string> extra in options.ExtraLabels) {
            // a duplicate or malformed label name 400s the whole push, so drop it instead.
            // session_name stays reserved: rimobs_session_info is its only carrier, and an
            // extra label of that name would put it back on the measurements.
            if (!IsValidLabelName(extra.Key) || HasKey(labels, extra.Key) || extra.Key == "session_name") {
                if (Interlocked.Exchange(ref _loggedBadLabels, 1) == 0)
                    _log?.LogWarning("Metrics push dropped extra label {Label}; names must match [a-zA-Z_][a-zA-Z0-9_]* and must not repeat session_id or session_name", extra.Key);
                continue;
            }

            labels.Add(extra);
        }

        List<RemoteWriteSample> series = [];
        void Add(string metric, double value) => series.Add(new RemoteWriteSample(metric, labels, value, nowMs));

        // the info-metric pattern: a constant 1 whose labels carry the identity. renaming a
        // session forks only this series, never the measurements.
        string name = _aggregator.SessionName;
        List<KeyValuePair<string, string>> infoLabels = [
            .. labels,
            new("session_name", string.IsNullOrWhiteSpace(name) ? sessionId : name),
        ];
        series.Add(new RemoteWriteSample("rimobs_session_info", infoLabels, 1, nowMs));

        if (_aggregator.HasTpsFps) {
            Add("rimobs_tps", _aggregator.LatestTps);
            Add("rimobs_fps", _aggregator.LatestFps);
        }

        double msPerTick = TickConverter.NsPerTick(_aggregator.Meta) / 1_000_000.0;

        PercentileSnapshot ticks = _aggregator.TickSectionStats?.Distribution.SnapshotPercentiles()
            ?? PercentileSnapshot.Empty;
        // Empty is all zeroes, which is also what a section with no samples yields, so skip both.
        if (ticks.P99Ticks > 0) {
            // the histogram behind these never resets, so they are session-to-date, not a window.
            // graph them as a time series and one hitch pins the line for the rest of the run.
            Add("rimobs_tick_ms_p50_session", ticks.P50Ticks * msPerTick);
            Add("rimobs_tick_ms_p99_session", ticks.P99Ticks * msPerTick);
        }

        FrameRingStats frames = _aggregator.Frames.ComputeStats();
        // the stats cover open frames too, so ordinals, not the sealed count, say whether any exist.
        if (frames.NewestOrdinal >= 0) {
            Add("rimobs_frame_ms_p50", frames.MedianDurationTicks * msPerTick);
            Add("rimobs_frame_ms_p99", frames.P99DurationTicks * msPerTick);
        }

        GcEventRecord[] gc = _aggregator.SnapshotGcEvents(1);
        if (gc.Length > 0)
            Add("rimobs_alloc_bytes_per_min", gc[0].AllocationRateBytesPerMinute);

        Add("rimobs_gc_pause_ms_total", _aggregator.TotalGcPauseMicros / 1_000.0);
        Add("rimobs_gc_events_total", _aggregator.TotalGcEvents);

        if (_aggregator.LatestVram is { } vram)
            Add("rimobs_vram_tracked_bytes", vram.Batch.TextureBytes + vram.Batch.MeshBytes + vram.Batch.RenderTargetBytes);

        Add("rimobs_samples_total", _aggregator.TotalSamples);
        Add("rimobs_transport_lost_total", _aggregator.LostDatagrams + _aggregator.BacklogDrops);
        return series;
    }

    /// <summary>The prometheus label-name rule, [a-zA-Z_][a-zA-Z0-9_]*.</summary>
    internal static bool IsValidLabelName(string name) {
        if (string.IsNullOrEmpty(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;

        for (int i = 1; i < name.Length; i++)
            if (!char.IsAsciiLetterOrDigit(name[i]) && name[i] != '_')
                return false;

        return true;
    }

    private static bool HasKey(List<KeyValuePair<string, string>> labels, string key) {
        for (int i = 0; i < labels.Count; i++)
            if (labels[i].Key == key)
                return true;

        return false;
    }
}
