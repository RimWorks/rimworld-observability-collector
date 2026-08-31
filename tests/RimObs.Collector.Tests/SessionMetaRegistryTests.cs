using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SessionMetaRegistryTests {
    [Fact]
    public void OnSessionMeta_records_control_port_secret_and_sessionId() {
        SessionMetaRegistry registry = new();
        SessionMeta meta = new() {
            SessionId = "session-1",
            ControlPort = 51234,
            ControlSecret = "secret-abc",
        };

        registry.OnSessionMeta(meta);

        registry.ControlPort.Should().Be(51234);
        registry.ControlSecret.Should().Be("secret-abc");
        registry.SessionId.Should().Be("session-1");
        registry.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_is_false_before_any_session_meta() {
        SessionMetaRegistry registry = new();

        registry.IsAvailable.Should().BeFalse();
        registry.ControlPort.Should().Be(0);
        registry.ControlSecret.Should().Be(string.Empty);
        registry.SessionId.Should().Be(string.Empty);
    }

    [Fact]
    public void OnSessionMeta_overwrites_previous_values() {
        SessionMetaRegistry registry = new();
        registry.OnSessionMeta(new SessionMeta {
            SessionId = "session-1",
            ControlPort = 51234,
            ControlSecret = "secret-abc",
        });

        registry.OnSessionMeta(new SessionMeta {
            SessionId = "session-2",
            ControlPort = 60000,
            ControlSecret = "secret-xyz",
        });

        registry.ControlPort.Should().Be(60000);
        registry.ControlSecret.Should().Be("secret-xyz");
        registry.SessionId.Should().Be("session-2");
    }
}
