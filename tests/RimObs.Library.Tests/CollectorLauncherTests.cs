using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RimWorks.RimObs.Transport;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class CollectorLauncherTests {
    [Fact]
    public void ParseLooseSemver_strips_prerelease_and_build_metadata() {
        CollectorCandidate.ParseLooseSemver("1.2.3").Should().Be(new Version(1, 2, 3));
        CollectorCandidate.ParseLooseSemver("1.2.3-beta.5").Should().Be(new Version(1, 2, 3));
        CollectorCandidate.ParseLooseSemver("2.0.1+build.99").Should().Be(new Version(2, 0, 1));
    }

    [Fact]
    public void BuildLaunchArguments_includes_port_and_parent_pid() {
        CollectorLauncher.BuildLaunchArguments(45678, 1234)
            .Should().Be("serve --port 45678 --parent-pid 1234");
    }

    [Fact]
    public void BuildLaunchArguments_omits_parent_pid_when_not_provided() {
        CollectorLauncher.BuildLaunchArguments(45678, 0)
            .Should().Be("serve --port 45678");
    }


    [Fact]
    public void BuildLaunchArguments_appends_no_browser_when_requested() {
        CollectorLauncher.BuildLaunchArguments(45678, 1234, noBrowser: true)
            .Should().Be("serve --port 45678 --parent-pid 1234 --no-browser");
    }

    [Fact]
    public void BuildLaunchArguments_omits_no_browser_by_default() {
        CollectorLauncher.BuildLaunchArguments(45678, 1234)
            .Should().Be("serve --port 45678 --parent-pid 1234");
    }

    [Fact]
    public void SelectHighest_returns_null_for_empty() {
        CollectorDiscovery.SelectHighest(new List<CollectorCandidate>()).Should().BeNull();
    }

    [Fact]
    public void SelectHighest_picks_greatest_version() {
        List<CollectorCandidate> candidates =
        [
            new("/a/Collector", new Version(1, 0, 0)),
            new("/b/Collector", new Version(2, 3, 1)),
            new("/c/Collector", new Version(2, 3, 0)),
        ];

        CollectorCandidate? best = CollectorDiscovery.SelectHighest(candidates);

        best.Should().NotBeNull();
        best!.Version.Should().Be(new Version(2, 3, 1));
        best.ExecutablePath.Should().Be("/b/Collector");
    }

    [Fact]
    public void Parse_splits_core_version_from_prerelease() {
        CollectorCandidate stable = CollectorCandidate.Parse("/x/Collector", "2.1.0");
        stable.Version.Should().Be(new Version(2, 1, 0));
        stable.IsPrerelease.Should().BeFalse();
        stable.Prerelease.Should().BeNull();

        CollectorCandidate pre = CollectorCandidate.Parse("/x/Collector", "2.1.0-beta.3+build.7");
        pre.Version.Should().Be(new Version(2, 1, 0));
        pre.IsPrerelease.Should().BeTrue();
        pre.Prerelease.Should().Be("beta.3");
    }

    [Fact]
    public void SelectHighest_prefers_stable_over_prerelease_when_core_versions_tie() {
        List<CollectorCandidate> candidates =
        [
            CollectorCandidate.Parse("/pre", "2.1.0-beta.3"),
            CollectorCandidate.Parse("/stable", "2.1.0"),
        ];

        CollectorCandidate? best = CollectorDiscovery.SelectHighest(candidates);

        best!.ExecutablePath.Should().Be("/stable");
        best.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public void SelectHighest_prerelease_with_higher_core_beats_lower_stable() {
        List<CollectorCandidate> candidates =
        [
            CollectorCandidate.Parse("/stable", "2.1.0"),
            CollectorCandidate.Parse("/pre", "2.2.0-beta.1"),
        ];

        CollectorCandidate? best = CollectorDiscovery.SelectHighest(candidates);

        best!.ExecutablePath.Should().Be("/pre");
        best.Version.Should().Be(new Version(2, 2, 0));
    }

    [Fact]
    public void EnsureRunning_returns_running_without_launching_when_collector_already_live() {
        using PongResponder responder = new("5.5.5", "already-up");
        bool launched = false;

        CollectorLaunchResult result = CollectorLauncher.EnsureRunning(
            [new CollectorCandidate("/never/run", new Version(1, 0, 0))],
            "127.0.0.1",
            responder.Port,
            "owner",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            _ => launched = true);

        result.IsRunning.Should().BeTrue();
        result.LaunchAttempted.Should().BeFalse();
        result.Pong!.CollectorVersion.Should().Be("5.5.5");
        launched.Should().BeFalse();
    }

    [Fact]
    public void EnsureRunning_returns_not_running_when_no_candidates_and_dead_port() {
        int deadPort = GetFreePort();

        CollectorLaunchResult result = CollectorLauncher.EnsureRunning(
            new List<CollectorCandidate>(),
            "127.0.0.1",
            deadPort,
            "owner",
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(200));

        result.IsRunning.Should().BeFalse();
        result.LaunchAttempted.Should().BeFalse();
        result.SelectedCandidate.Should().BeNull();
    }

    [Fact]
    public void EnsureRunning_launches_selected_candidate_then_succeeds_once_responder_comes_up() {
        int port = GetFreePort();
        PongResponder? responder = null;
        try {
            CollectorLaunchResult result = CollectorLauncher.EnsureRunning(
                [
                    new CollectorCandidate("/low", new Version(1, 0, 0)),
                    new CollectorCandidate("/high", new Version(3, 0, 0)),
                ],
                "127.0.0.1",
                port,
                "owner",
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromSeconds(3),
                candidate => {
                    candidate.ExecutablePath.Should().Be("/high");
                    responder = new PongResponder("7.0.0", "spawned", port);
                });

            result.IsRunning.Should().BeTrue();
            result.LaunchAttempted.Should().BeTrue();
            result.SelectedCandidate!.ExecutablePath.Should().Be("/high");
            result.Pong!.CollectorVersion.Should().Be("7.0.0");
        }
        finally {
            responder?.Dispose();
        }
    }

    [Fact]
    public void EnsureRunning_reports_failure_when_launch_never_produces_a_pong() {
        int deadPort = GetFreePort();
        bool launched = false;

        CollectorLaunchResult result = CollectorLauncher.EnsureRunning(
            [new CollectorCandidate("/x", new Version(1, 0, 0))],
            "127.0.0.1",
            deadPort,
            "owner",
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(400),
            _ => launched = true);

        launched.Should().BeTrue();
        result.IsRunning.Should().BeFalse();
        result.LaunchAttempted.Should().BeTrue();
    }

    [Fact]
    public void EnsureRunning_reports_failure_when_launch_action_throws() {
        int deadPort = GetFreePort();
        InvalidOperationException thrown = new("launch failed");

        CollectorLaunchResult result = CollectorLauncher.EnsureRunning(
            [new CollectorCandidate("/x", new Version(1, 0, 0))],
            "127.0.0.1",
            deadPort,
            "owner",
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(400),
            _ => throw thrown);

        result.IsRunning.Should().BeFalse();
        result.LaunchAttempted.Should().BeTrue();
        result.LaunchError.Should().BeSameAs(thrown);
    }

    private static int GetFreePort() {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private sealed class PongResponder : IDisposable {
        private readonly UdpClient _client;
        private readonly Thread _thread;
        private readonly string _version;
        private readonly string? _sessionId;
        private volatile bool _running = true;

        public PongResponder(string version, string? sessionId, int? port = null) {
            _version = version;
            _sessionId = sessionId;
            _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port ?? 0));
            Port = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public int Port { get; }

        private void Loop() {
            _client.Client.ReceiveTimeout = 150;
            while (_running) {
                IPEndPoint remote = new(IPAddress.Any, 0);
                byte[] datagram;
                try {
                    datagram = _client.Receive(ref remote);
                }
                catch (SocketException) {
                    continue;
                }
                catch (ObjectDisposedException) {
                    break;
                }

                try {
                    TelemetryBatch envelope = WireCodec.Deserialize<TelemetryBatch>(datagram);
                    if (envelope.BatchType != BatchType.Ping)
                        continue;
                    PingMessage ping = WireCodec.Deserialize<PingMessage>(envelope.Payload);
                    PongMessage pong = new() {
                        OwnerId = ping.OwnerId,
                        PingSentAtUtcTicks = ping.SentAtUtcTicks,
                        CollectorVersion = _version,
                        SessionId = _sessionId,
                    };
                    TelemetryBatch pongEnvelope = new() {
                        SchemaVersion = SchemaVersion.Current,
                        Sequence = 0,
                        OwnerId = "collector",
                        BatchType = BatchType.Pong,
                        Payload = WireCodec.Serialize(pong),
                    };
                    byte[] bytes = WireCodec.Serialize(pongEnvelope);
                    _client.Send(bytes, bytes.Length, remote);
                }
                catch (WireFormatException) {
                    // Ignore stray/malformed datagrams.
                }
            }
        }

        public void Dispose() {
            _running = false;
            _client.Dispose();
            _thread.Join(TimeSpan.FromSeconds(1));
        }
    }
}
