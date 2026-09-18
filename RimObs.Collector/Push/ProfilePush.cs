using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;

namespace RimWorks.RimObs.Collector.Push;

/// <summary>Ships the call tree to pyroscope as folded profiles, one per push window.</summary>
public sealed class ProfilePush(HttpClient http) {
    public const string AppName = "rimobs";

    private readonly Dictionary<(int Parent, int Section), long> _lastTicks = [];
    private DateTimeOffset _windowStart;
    private string? _sessionId;

    /// <summary>Drops the running totals so the next window measures from zero again.</summary>
    public void Reset() {
        _lastTicks.Clear();
        _windowStart = default;
    }

    public async Task PushAsync(
        SessionAggregator aggregator,
        MetricsPushOptions options,
        string sessionId,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(options.ProfileEndpoint) || string.IsNullOrWhiteSpace(sessionId))
            return;

        // a new session restarts the totals, so carrying the old ones over would push a
        // negative first window and drop every edge.
        if (sessionId != _sessionId) {
            Reset();
            _sessionId = sessionId;
        }

        DateTimeOffset until = DateTimeOffset.UtcNow;
        DateTimeOffset from = _windowStart == default ? until.AddSeconds(-options.IntervalSeconds) : _windowStart;
        _windowStart = until;

        List<ProfileEdge> deltas = Delta(aggregator.SnapshotCallEdges());
        if (deltas.Count == 0)
            return;

        Dictionary<int, string> names = [];
        foreach (SectionStats section in aggregator.SnapshotSections())
            names[section.SectionId] = section.Name;

        string folded = FoldedProfile.Encode(deltas, names, NsPerTick(aggregator));
        if (folded.Length == 0)
            return;

        await PostAsync(options, sessionId, folded, from, until, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Edge totals run cumulative for the session, but a window only owns what moved
    /// inside it.</summary>
    private List<ProfileEdge> Delta(IReadOnlyCollection<CallEdgeStats> edges) {
        List<ProfileEdge> deltas = [];
        foreach (CallEdgeStats edge in edges) {
            (int, int) key = (edge.ParentId, edge.SectionId);
            _lastTicks.TryGetValue(key, out long previous);
            _lastTicks[key] = edge.TotalElapsedTicks;

            long moved = edge.TotalElapsedTicks - previous;
            if (moved > 0)
                deltas.Add(new ProfileEdge(edge.ParentId, edge.SectionId, moved));
        }

        return deltas;
    }

    private async Task PostAsync(
        MetricsPushOptions options,
        string sessionId,
        string folded,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken) {
        using HttpRequestMessage request = new(HttpMethod.Post, IngestUrl(options, sessionId, from, until)) {
            Content = new StringContent(folded, Encoding.UTF8, "text/plain"),
        };
        if (!string.IsNullOrWhiteSpace(options.ProfileBasicAuth)) {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.ProfileBasicAuth));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    internal static string IngestUrl(
        MetricsPushOptions options,
        string sessionId,
        DateTimeOffset from,
        DateTimeOffset until) {
        List<KeyValuePair<string, string>> labels = [new("session_id", sessionId)];
        foreach (KeyValuePair<string, string> extra in options.ExtraLabels.OrderBy(p => p.Key, StringComparer.Ordinal)) {
            if (extra.Key != "session_id")
                labels.Add(extra);
        }

        string tags = string.Join(",", labels.Select(l => $"{l.Key}={Uri.EscapeDataString(l.Value)}"));
        string name = Uri.EscapeDataString(AppName) + "{" + tags + "}";
        string root = options.ProfileEndpoint.TrimEnd('/');

        return $"{root}/ingest?name={name}"
            + $"&from={from.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"
            + $"&until={until.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"
            + $"&sampleRate={FoldedProfile.SampleRateHz.ToString(CultureInfo.InvariantCulture)}"
            + "&format=folded&units=samples&aggregationType=sum&spyName=rimobs";
    }

    private static double NsPerTick(SessionAggregator aggregator) {
        long frequency = aggregator.Meta?.StopwatchFrequency ?? 0;
        return frequency > 0 ? 1_000_000_000.0 / frequency : 0;
    }
}
