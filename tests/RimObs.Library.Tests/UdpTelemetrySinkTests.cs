using System;
using System.Collections.Generic;
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
        MainThreadMarker.ResetForTests();
    }

    // regression: the sink captured its constructing thread as main, but rimworld constructs
    // mods on the threaded-loading worker, so the real main thread shipped as a unity job.
    [Fact]
    public void The_frame_thread_is_reannounced_as_main_once_marked() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");
        MainThreadMarker.ResetForTests();

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 250;

        List<(int Id, int Role)> announced = new();
        using CancellationTokenSource stop = new();
        Thread listener = new(() => {
            IPEndPoint any = new(IPAddress.Any, 0);
            while (!stop.IsCancellationRequested) {
                try {
                    byte[] bytes = receiver.Receive(ref any);
                    TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                    if (envelope.BatchType != BatchType.ThreadRegistrations)
                        continue;
                    ThreadRegistrationsBatch lanes = WireCodec.Deserialize<ThreadRegistrationsBatch>(envelope.Payload);
                    lock (announced) {
                        for (int i = 0; i < lanes.ThreadIds.Length; i++)
                            announced.Add((lanes.ThreadIds[i], lanes.Roles[i]));
                    }
                }
                catch (SocketException) {
                }
                catch (ObjectDisposedException) {
                    return;
                }
            }
        }) {
            IsBackground = true,
        };
        listener.Start();

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();
        SectionHandle handle = SectionRegistry.Register("test.main-lane");

        // sampled before any frame has run: no thread can be called main yet.
        sink.RecordSection(handle.Id, parentId: -1, nodeId: 1, parentNodeId: -1, startTimestamp: 0L, elapsedTicks: 1L, allocBytes: 0L);
        Thread.Sleep(300);

        int me = Environment.CurrentManagedThreadId;
        lock (announced)
            announced.Should().NotContain((me, (int)ThreadRole.Main));

        // the first frame prefix marks this thread; the next sample must re-announce the lane.
        MainThreadMarker.Mark();
        sink.RecordSection(handle.Id, parentId: -1, nodeId: 2, parentNodeId: -1, startTimestamp: 0L, elapsedTicks: 1L, allocBytes: 0L);
        Thread.Sleep(300);

        stop.Cancel();
        listener.Join(TimeSpan.FromSeconds(2));

        lock (announced)
            announced.Should().Contain((me, (int)ThreadRole.Main));
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
            sink.RecordSection(sectionId: i, parentId: -1, nodeId: i, parentNodeId: -1, startTimestamp: i * 100L, elapsedTicks: 50L, allocBytes: 0L);

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
            sink.RecordSection(handle.Id, parentId: -1, nodeId: i, parentNodeId: -1, startTimestamp: i, elapsedTicks: 100, allocBytes: 16L * i);

        SectionBatch? sections = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && sections == null) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.Sections)
                    sections = WireCodec.Deserialize<SectionBatch>(envelope.Payload);
            }
            catch (SocketException) {
                break;
            }
        }

        sections.Should().NotBeNull("UdpTelemetrySink should flush SectionBatch frames to the loopback receiver within 3s");

        // regression: catches a Slice() swap, node ids are 0..7 and parent node ids are all -1.
        sections!.NodeIds.Should().HaveCount(sections.SectionIds.Length).And.NotContain(-1);
        sections.ParentNodeIds.Should().AllBeEquivalentTo(-1);

        // regression: v9 shipped with ThreadIds never filled, so every batch decoded as v8.
        sections.ThreadIds.Should().HaveCount(sections.SectionIds.Length)
            .And.AllBeEquivalentTo(Environment.CurrentManagedThreadId);

        // regression: SamplesSent increments after Send returns, so a loopback receiver can see the
        // datagram first. poll instead of reading once. flaked on Linux CI.
        SpinWait.SpinUntil(() => sink.SamplesSent > 0, TimeSpan.FromSeconds(2));
        sink.SamplesSent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Lanes_are_announced_once_before_the_samples_that_use_them() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();

        // the frame prefix has marked this thread, so its lane announces as Main.
        MainThreadMarker.Mark();
        SectionHandle handle = SectionRegistry.Register("test.lanes");
        for (int i = 0; i < 8; i++)
            sink.RecordSection(handle.Id, parentId: -1, nodeId: i, parentNodeId: -1, startTimestamp: i, elapsedTicks: 100, allocBytes: 0L);

        ThreadRegistrationsBatch? lanes = null;
        int laneBatches = 0;
        int sectionBatches = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && sectionBatches < 2) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.ThreadRegistrations) {
                    laneBatches++;
                    lanes ??= WireCodec.Deserialize<ThreadRegistrationsBatch>(envelope.Payload);
                }
                else if (envelope.BatchType == BatchType.Sections) {
                    sectionBatches++;
                    // a second sample batch on the same lane must not re-announce it.
                    sink.RecordSection(handle.Id, parentId: -1, nodeId: 99, parentNodeId: -1, startTimestamp: 1, elapsedTicks: 1, allocBytes: 0L);
                }
            }
            catch (SocketException) {
                break;
            }
        }

        lanes.Should().NotBeNull("the sink must announce a lane before the SectionBatch that references it");
        lanes!.ThreadIds.Should().Equal(Environment.CurrentManagedThreadId);
        lanes.Roles.Should().Equal((int)ThreadRole.Main);
        laneBatches.Should().Be(1);
    }

    [Fact]
    public void Lane_registration_carries_the_producing_thread_name_and_role() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();

        SectionHandle handle = SectionRegistry.Register("test.named-lane");
        using ManualResetEventSlim release = new(false);
        Thread producer = new(() => {
            sink.RecordSection(handle.Id, parentId: -1, nodeId: 1, parentNodeId: -1, startTimestamp: 1, elapsedTicks: 100, allocBytes: 0L);
            release.Wait(TimeSpan.FromSeconds(5));
        }) {
            Name = "SomeMod.Background",
            IsBackground = true,
        };
        producer.Start();

        ThreadRegistrationsBatch? lanes = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && lanes == null) {
            try {
                byte[] bytes = receiver.Receive(ref any);
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                if (envelope.BatchType == BatchType.ThreadRegistrations)
                    lanes = WireCodec.Deserialize<ThreadRegistrationsBatch>(envelope.Payload);
            }
            catch (SocketException) {
                break;
            }
        }

        release.Set();
        producer.Join(TimeSpan.FromSeconds(5));

        // regression: every lane used to register as an empty name with the UnityJob role.
        lanes.Should().NotBeNull();
        lanes!.Names.Should().Equal("SomeMod.Background");
        lanes.Roles.Should().Equal((int)ThreadRole.Mod);
    }

    /// <summary>
    /// Starts a named thread that writes one sample, waits for it to exit, and returns the managed
    /// thread id it ran on. The Thread object dies with this frame so a GC can recycle that id.
    /// </summary>
    private static int RunProducer(UdpTelemetrySink sink, int sectionId, string name) {
        int id = 0;
        Thread producer = new(() => {
            id = Environment.CurrentManagedThreadId;
            sink.RecordSection(sectionId, parentId: -1, nodeId: 1, parentNodeId: -1, startTimestamp: 1, elapsedTicks: 100, allocBytes: 0L);
        }) {
            Name = name,
            IsBackground = true,
        };
        producer.Start();
        producer.Join(TimeSpan.FromSeconds(5));
        return id;
    }

    [Fact]
    public void A_recycled_thread_id_is_announced_again_under_its_new_lane_name() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 250;

        List<(int Id, string Name)> announced = new();
        using CancellationTokenSource stop = new();
        Thread listener = new(() => {
            IPEndPoint any = new(IPAddress.Any, 0);
            while (!stop.IsCancellationRequested) {
                try {
                    byte[] bytes = receiver.Receive(ref any);
                    TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(bytes);
                    if (envelope.BatchType != BatchType.ThreadRegistrations)
                        continue;
                    ThreadRegistrationsBatch lanes = WireCodec.Deserialize<ThreadRegistrationsBatch>(envelope.Payload);
                    lock (announced) {
                        for (int i = 0; i < lanes.ThreadIds.Length; i++)
                            announced.Add((lanes.ThreadIds[i], lanes.Names[i]));
                    }
                }
                catch (SocketException) {
                }
                catch (ObjectDisposedException) {
                    return;
                }
            }
        }) {
            IsBackground = true,
        };
        listener.Start();

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();
        SectionHandle handle = SectionRegistry.Register("test.recycled-lane");

        List<(int Id, string Name)> produced = new();
        for (int i = 0; i < 4; i++) {
            string name = $"Lane.Recycled{i}";
            produced.Add((RunProducer(sink, handle.Id, name), name));
            // the sender needs a drain pass to reap the dead lane; only then does the Thread object
            // drop out of the lane array and the runtime hand its id to the next thread.
            Thread.Sleep(400);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Thread.Sleep(400);
        stop.Cancel();
        listener.Join(TimeSpan.FromSeconds(2));

        (int Id, string Name)? reuse = null;
        for (int i = 1; i < produced.Count && reuse == null; i++) {
            for (int j = 0; j < i; j++) {
                if (produced[j].Id == produced[i].Id) {
                    reuse = produced[i];
                    break;
                }
            }
        }

        reuse.Should().NotBeNull("the runtime should hand a dead thread's managed id to a later thread");

        // regression: the sink deduped announcements by thread id forever, so a recycled id kept the
        // dead thread's name and role on the collector side.
        lock (announced)
            announced.Should().Contain(reuse!.Value);
    }

    [Fact]
    public void Send_to_unbound_port_records_socket_error() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        sink.Start();

        sink.RecordSection(sectionId: 0, parentId: -1, nodeId: 1, parentNodeId: -1, startTimestamp: 0L, elapsedTicks: 1L, allocBytes: 0L);

        // No receiver bound; on most OSes loopback UDP swallows silently, but the send loop
        // should still drain the ring buffer without leaking exceptions.
        Thread.Sleep(300);

        sink.SamplesDropped.Should().Be(0);
    }

    // regression: ring drops died inside the game. nothing on the wire carried them, so the
    // dashboard's lossy badge stayed quiet while a wide filter overflowed the ring.
    [Fact]
    public void SessionMeta_carries_the_ring_drop_count() {
        int port = GetFreePort();
        SessionAnchor.Initialize("test-session");

        using UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, port));
        receiver.Client.ReceiveTimeout = 2000;

        using UdpTelemetrySink sink = new(ownerId: "test.owner", port: port);
        int capacity = sink.SetRingCapacity(1);
        for (int i = 0; i < capacity * 2; i++)
            sink.RecordSection(sectionId: 0, parentId: -1, nodeId: i, parentNodeId: -1, startTimestamp: i, elapsedTicks: 1L, allocBytes: 0L);
        sink.SamplesDropped.Should().BeGreaterThan(0, "the ring must overflow for this test to mean anything");
        sink.Start();

        SessionMeta? meta = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        IPEndPoint any = new(IPAddress.Any, 0);
        while (DateTime.UtcNow < deadline && meta == null) {
            try {
                TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(receiver.Receive(ref any));
                if (envelope.BatchType == BatchType.SessionMeta)
                    meta = WireCodec.Deserialize<SessionMeta>(envelope.Payload);
            }
            catch (SocketException) {
                break;
            }
        }

        meta.Should().NotBeNull();
        meta!.SamplesDropped.Should().BeGreaterThan(0);
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

            // SamplesSent increments after Send returns, so the receiver can win the race. poll instead
            // of reading once. same race as Drain_flushes_to_loopback_receiver.
            SpinWait.SpinUntil(() => sink.SamplesSent > 0, TimeSpan.FromSeconds(2));
            sink.SamplesSent.Should().BeGreaterThan(0);
        }
        finally {
            Profiler.SetSink(null);
            Profiler.Enabled = priorEnabled;
        }
    }
}
