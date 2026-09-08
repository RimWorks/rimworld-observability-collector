using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Metrics;
using RimWorks.RimObs.Transport;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class MetricsFlushTests : IDisposable {
    private const string TestPackageId = "RimWorks.RimObs.tests";

    public MetricsFlushTests() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
        OwnerRegistry.RegisterMod(typeof(MetricsFlushTests).Assembly, TestPackageId);
    }

    public void Dispose() {
        OwnerRegistry.Clear();
        MetricRegistry.Clear();
    }

    private static int GetFreePort() {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static T? Receive<T>(UdpClient receiver, BatchType type, Func<T, bool> match, int seconds = 3)
        where T : class {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType != type)
                    continue;
                T payload = WireCodec.Deserialize<T>(envelope.Payload);
                if (match(payload))
                    return payload;
            }
            catch (SocketException) {
                return null;
            }
        }
        return null;
    }

    [Fact]
    public void Registered_counter_and_its_value_reach_the_receiver() {
        int port = GetFreePort();
        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        CounterHandle handle = Obs.Metrics.RegisterCounter("things_spawned", unit: "count");
        Obs.Metrics.Add(handle, 3);
        sink.Start();

        MetricRegistrationsBatch? registrations = Receive<MetricRegistrationsBatch>(
            receiver,
            BatchType.MetricRegistrations,
            b => Array.IndexOf(b.MetricIds, handle.Id) >= 0
        );
        registrations.Should().NotBeNull("the sink must publish metric registrations, not just section ones");
        int at = Array.IndexOf(registrations!.MetricIds, handle.Id);
        registrations.Names[at].Should().Be(TestPackageId + ".things_spawned");
        registrations.Kinds[at].Should().Be((byte)MetricKind.Counter);
        registrations.Units[at].Should().Be("count");

        MetricsBatch? values = Receive<MetricsBatch>(
            receiver,
            BatchType.Metrics,
            b => Array.IndexOf(b.MetricIds, handle.Id) >= 0
        );
        values.Should().NotBeNull("Obs.Metrics.Add must reach the collector, it silently did nothing before");
        int i = Array.IndexOf(values!.MetricIds, handle.Id);
        values.LabelCanonicals[i].Should().BeEmpty();
        values.Kinds[i].Should().Be((byte)MetricKind.Counter);
        values.Values[i].Should().Be(3);
        values.SampleCounts[i].Should().Be(3);
    }

    [Fact]
    public void Labeled_values_flush_and_collapse_into_overflow_at_the_cardinality_limit() {
        int port = GetFreePort();
        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        CounterHandle handle = Obs.Metrics.RegisterCounter("requests", cardinalityLimit: 2);
        Obs.Metrics.Add(handle, 1, "outcome", "ok");
        Obs.Metrics.Add(handle, 1, "outcome", "fail");
        Obs.Metrics.Add(handle, 1, "outcome", "other");
        sink.Start();

        MetricsBatch? values = Receive<MetricsBatch>(
            receiver,
            BatchType.Metrics,
            b => {
                for (int i = 0; i < b.MetricIds.Length; i++) {
                    if (b.MetricIds[i] == handle.Id && b.LabelCanonicals[i] == MetricDescriptor.OverflowLabel)
                        return true;
                }
                return false;
            }
        );

        values.Should().NotBeNull("labeled metrics past the cardinality limit must still flush, as __overflow");

        List<string> canonicals = [];
        for (int i = 0; i < values!.MetricIds.Length; i++) {
            if (values.MetricIds[i] == handle.Id)
                canonicals.Add(values.LabelCanonicals[i]);
        }
        canonicals.Should().Contain("outcome=ok").And.Contain("outcome=fail").And.Contain(MetricDescriptor.OverflowLabel);
        canonicals.Should().NotContain("outcome=other");
    }

    [Fact]
    public void Histogram_sample_counts_are_sent_as_deltas_and_values_stay_cumulative() {
        int port = GetFreePort();
        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        HistogramHandle handle = Obs.Metrics.RegisterHistogram("search_duration_us", unit: "us");
        Obs.Metrics.Observe(handle, 5);
        sink.Start();

        MetricsBatch? first = Receive<MetricsBatch>(receiver, BatchType.Metrics, b => Array.IndexOf(b.MetricIds, handle.Id) >= 0);
        first.Should().NotBeNull();
        int a = Array.IndexOf(first!.MetricIds, handle.Id);
        first.Values[a].Should().Be(5);
        first.SampleCounts[a].Should().Be(1);

        Obs.Metrics.Observe(handle, 7);
        Obs.Metrics.Observe(handle, 9);

        // a drain tick can land between the two Observe calls, so sum the deltas instead of
        // demanding one batch carry both.
        long samples = 0;
        long latest = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && latest != 21) {
            MetricsBatch? next = Receive<MetricsBatch>(receiver, BatchType.Metrics, b => Array.IndexOf(b.MetricIds, handle.Id) >= 0, seconds: 1);
            if (next == null)
                break;
            int c = Array.IndexOf(next.MetricIds, handle.Id);
            samples += next.SampleCounts[c];
            latest = next.Values[c];
        }

        latest.Should().Be(21);
        samples.Should().Be(2, "sample counts accumulate on the collector, so the wire carries the delta");
    }

    [Fact]
    public void Quiet_metrics_stop_sending_once_flushed() {
        int port = GetFreePort();
        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 800;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        GaugeHandle handle = Obs.Metrics.RegisterGauge("pending_jobs");
        Obs.Metrics.Set(handle, 4);
        sink.Start();

        Receive<MetricsBatch>(receiver, BatchType.Metrics, b => Array.IndexOf(b.MetricIds, handle.Id) >= 0)
            .Should().NotBeNull();

        MetricsBatch? repeat = Receive<MetricsBatch>(
            receiver,
            BatchType.Metrics,
            b => Array.IndexOf(b.MetricIds, handle.Id) >= 0,
            seconds: 1
        );
        repeat.Should().BeNull("an unchanged gauge must not re-send every 100ms drain tick");
    }
}
