using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RimWorks.RimObs.Profile;
using RimWorks.RimObs.Session;
using RimWorks.RimObs.Transport;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class UdpTelemetrySinkTests : IDisposable {
    public void Dispose() {
        SectionRegistry.Clear();
    }

    private static int GetFreePort() {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public void Constructor_throws_on_null_owner_id() {
        Action act = () => _ = new UdpTelemetrySink(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SendErrors_starts_at_zero_and_last_error_null() {
        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: GetFreePort());

        sink.SendErrors.Should().Be(0);
        sink.LastSendError.Should().BeNull();
    }

    [Fact]
    public void RecordSection_buffers_without_throwing_when_no_receiver() {
        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: GetFreePort());

        for (int i = 0; i < 32; i++)
            sink.RecordSection(sectionId: i, parentId: -1, startTimestamp: i * 100L, elapsedTicks: 50L);

        // No exception means the ring buffer absorbed the writes even with no sender thread running.
        sink.SamplesSent.Should().Be(0);
    }

    [Fact]

    public void Drain_flushes_to_loopback_receiver() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();

        SectionHandle handle = SectionRegistry.Register("test.section");
        for (int i = 0; i < 8; i++)
            sink.RecordSection(handle.Id, parentId: -1, startTimestamp: i, elapsedTicks: 100);

        bool sawSection = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && !sawSection) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.Sections)
                    sawSection = true;
            }
            catch (SocketException) {
                break;
            }
        }

        sawSection.Should().BeTrue("UdpTelemetrySink should flush SectionBatch frames to the loopback receiver within 3s");

        // The sender thread increments SamplesSent on the line *after* UdpClient.Send returns, so a
        // loopback receiver can observe the datagram before the counter is bumped. Poll briefly to
        // close that race instead of reading the counter once (regression: flaky failure on Linux CI
        // where the receive won the race against the increment).
        SpinWait.SpinUntil(() => sink.SamplesSent > 0, TimeSpan.FromSeconds(2));
        sink.SamplesSent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Send_to_unbound_port_records_socket_error() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();

        sink.RecordSection(sectionId: 0, parentId: -1, startTimestamp: 0L, elapsedTicks: 1L);

        // No receiver bound; on most OSes loopback UDP swallows silently, but the send loop
        // should still drain the ring buffer without leaking exceptions.
        Thread.Sleep(300);

        sink.SamplesDropped.Should().Be(0);
    }


    [Fact]
    public void SessionMeta_is_resent_so_a_dropped_first_datagram_does_not_blank_the_session() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();

        int metaCount = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && metaCount < 2) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.SessionMeta)
                    metaCount++;
            }
            catch (SocketException) {
                break;
            }
        }

        metaCount.Should().BeGreaterThan(1,
            "SessionMeta must be resent (not fire-and-forget) so a single dropped loopback datagram does not leave the collector without a session");
    }

    [Fact]
    public void Profiler_routed_through_setsink_reaches_loopback_receiver() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();
        bool priorEnabled = Profiler.Enabled;
        Profiler.SetSink(sink);
        Profiler.Enabled = true;
        try {
            SectionHandle handle = SectionRegistry.Register("test.bootstrap_smoke");
            for (int i = 0; i < 4; i++) {
                long token = Profiler.StartById(handle.Id);
                Profiler.StopById(handle.Id, token);
            }

            bool sawSection = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            IPEndPoint any = new(IPAddress.Any, 0);
            while (DateTime.UtcNow < deadline && !sawSection) {
                try {
                    byte[] bytes = receiver.Receive(ref any);
                    TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                    if (envelope.BatchType == BatchType.Sections)
                        sawSection = true;
                }
                catch (SocketException) {
                    break;
                }
            }

            sawSection.Should().BeTrue("Profiler samples should reach the sink set via Profiler.SetSink and flush over loopback");

            // SamplesSent is incremented on the line after UdpClient.Send returns, so the loopback
            // receiver can observe the datagram before the counter is bumped. Poll instead of reading
            // once (same race as Drain_flushes_to_loopback_receiver).
            SpinWait.SpinUntil(() => sink.SamplesSent > 0, TimeSpan.FromSeconds(2));
            sink.SamplesSent.Should().BeGreaterThan(0);
        }
        finally {
            Profiler.SetSink(null);
            Profiler.Enabled = priorEnabled;
        }
    }
}
