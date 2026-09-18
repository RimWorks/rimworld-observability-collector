using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Config;
using RimWorks.RimObs.Collector.Push;
using RimWorks.RimObs.Wire;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ProfilePushTests {
    private const string Endpoint = "http://pyroscope.example:4040";

    private static SessionAggregator Aggregator(string sessionId = "sess-1") {
        SessionAggregator agg = new();
        agg.OnSessionMeta(new SessionMeta { SessionId = sessionId, StopwatchFrequency = 1_000_000 });
        agg.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [10, 20],
            Names = ["tree.root", "tree.child"],
            Subsystems = ["ticks", null],
        });
        return agg;
    }

    private static void Feed(SessionAggregator agg, long rootTicks, long childTicks) =>
        agg.OnSectionBatch(new SectionBatch {
            SectionIds = [10, 20],
            ParentIds = [-1, 10],
            StartTimestamps = [1, 2],
            ElapsedTicks = [rootTicks, childTicks],
        });

    private static MetricsPushOptions Options() => new() {
        Enabled = true,
        ProfileEndpoint = Endpoint,
        ProfileBasicAuth = "rimobs:hunter2",
        IntervalSeconds = 10,
    };

    [Fact]
    public async Task A_window_posts_folded_stacks_with_basic_auth() {
        FakePyroscope handler = new();
        ProfilePush push = new(new HttpClient(handler));
        SessionAggregator agg = Aggregator();
        Feed(agg, 1000, 400);

        await push.PushAsync(agg, Options(), "sess-1", CancellationToken.None);

        Sent sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Url.Should().StartWith($"{Endpoint}/ingest?name=");
        sent.Url.Should().Contain("format=folded").And.Contain("sampleRate=1000000");
        sent.Url.Should().Contain("session_id=sess-1");
        sent.Authorization.Should().Be("Basic " + Convert.ToBase64String("rimobs:hunter2"u8.ToArray()));
        sent.Body.Should().Contain("tree.root").And.Contain("tree.root;tree.child");
    }

    // edge totals are cumulative for the session, so a window that resent them would count
    // the same microseconds again on every tick.
    [Fact]
    public async Task Each_window_sends_only_what_moved_since_the_last_one() {
        FakePyroscope handler = new();
        ProfilePush push = new(new HttpClient(handler));
        SessionAggregator agg = Aggregator();
        MetricsPushOptions options = Options();

        Feed(agg, 1000, 400);
        await push.PushAsync(agg, options, "sess-1", CancellationToken.None);
        Feed(agg, 200, 100);
        await push.PushAsync(agg, options, "sess-1", CancellationToken.None);

        handler.Sent.Should().HaveCount(2);
        // 1000 root ticks minus its 400-tick child is 600 self micros at 1 tick per micro.
        handler.Sent[0].Body.Should().Contain("tree.root 600");
        handler.Sent[1].Body.Should().Contain("tree.root 100");
    }

    [Fact]
    public async Task A_window_with_no_movement_posts_nothing() {
        FakePyroscope handler = new();
        ProfilePush push = new(new HttpClient(handler));
        SessionAggregator agg = Aggregator();
        MetricsPushOptions options = Options();

        Feed(agg, 1000, 400);
        await push.PushAsync(agg, options, "sess-1", CancellationToken.None);
        await push.PushAsync(agg, options, "sess-1", CancellationToken.None);

        handler.Sent.Should().ContainSingle("the second window saw no new ticks");
    }

    // a fresh session zeroes the aggregator's totals, so keeping the old ones would make every
    // delta negative and silence the profile for the rest of the run.
    [Fact]
    public async Task A_new_session_starts_counting_from_zero_again() {
        FakePyroscope handler = new();
        ProfilePush push = new(new HttpClient(handler));
        MetricsPushOptions options = Options();

        SessionAggregator first = Aggregator();
        Feed(first, 5000, 1000);
        await push.PushAsync(first, options, "sess-1", CancellationToken.None);

        SessionAggregator second = Aggregator("sess-2");
        Feed(second, 800, 300);
        await push.PushAsync(second, options, "sess-2", CancellationToken.None);

        handler.Sent.Should().HaveCount(2);
        handler.Sent[1].Body.Should().Contain("tree.root 500");
        handler.Sent[1].Url.Should().Contain("sess-2");
    }

    [Fact]
    public async Task No_endpoint_means_no_profile_calls() {
        FakePyroscope handler = new();
        ProfilePush push = new(new HttpClient(handler));
        SessionAggregator agg = Aggregator();
        Feed(agg, 1000, 400);

        MetricsPushOptions options = Options();
        options.ProfileEndpoint = string.Empty;
        await push.PushAsync(agg, options, "sess-1", CancellationToken.None);

        handler.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task No_auth_configured_sends_no_authorization_header() {
        FakePyroscope handler = new();
        ProfilePush push = new(new HttpClient(handler));
        SessionAggregator agg = Aggregator();
        Feed(agg, 1000, 400);

        MetricsPushOptions options = Options();
        options.ProfileBasicAuth = string.Empty;
        await push.PushAsync(agg, options, "sess-1", CancellationToken.None);

        handler.Sent.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    [Fact]
    public void Extra_labels_ride_along_but_never_overwrite_the_session() {
        MetricsPushOptions options = Options();
        options.ExtraLabels["host"] = "desk";
        options.ExtraLabels["session_id"] = "spoofed";

        string url = ProfilePush.IngestUrl(
            options, "sess-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(10));

        url.Should().Contain("host=desk").And.Contain("session_id=sess-1");
        url.Should().NotContain("spoofed");
        url.Should().Contain("from=0").And.Contain("until=10");
    }

    private sealed record Sent(string Url, string? Authorization, string Body);

    private sealed class FakePyroscope : HttpMessageHandler {
        public List<Sent> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Sent.Add(new Sent(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
