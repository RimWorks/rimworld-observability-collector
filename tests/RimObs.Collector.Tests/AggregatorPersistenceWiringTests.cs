using System;
using System.IO;
using System.Linq;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Hosting;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

/// <summary>
/// The aggregator takes an optional persister, so a registration that calls the parameterless
/// constructor still compiles and still serves every endpoint, while writing nothing to disk.
/// Sessions then vanish on restart with no error anywhere. This pins the wiring itself.
/// </summary>
public sealed class AggregatorPersistenceWiringTests : IDisposable {
    private readonly string _sessionsDir;

    public AggregatorPersistenceWiringTests() {
        _sessionsDir = Path.Combine(Path.GetTempPath(), "rimobs-wiring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionsDir);
    }

    public void Dispose() {
        SqliteConnection.ClearAllPools();
        try {
            Directory.Delete(_sessionsDir, recursive: true);
        }
        catch (IOException) {
            // SQLite can briefly hold the file handle open through its pool.
        }
    }

    [Fact]
    public void A_session_reaches_disk_when_a_sessions_directory_is_configured() {
        WebApplication app = Program.BuildApp([], PickFreePort(), sessionsDir: _sessionsDir);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();

        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "wiring-check",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
            StartedUtcTicks = 1000L,
        });

        Directory.EnumerateFiles(_sessionsDir, "*.db")
            .Should().ContainSingle(f => Path.GetFileName(f).StartsWith("wiring-check", StringComparison.Ordinal));
    }

    [Fact]
    public void The_catalog_can_read_back_a_session_the_aggregator_wrote() {
        WebApplication app = Program.BuildApp([], PickFreePort(), sessionsDir: _sessionsDir);
        SessionAggregator aggregator = app.Services.GetRequiredService<SessionAggregator>();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "catalog-check",
            StopwatchFrequency = 10_000_000L,
            AnchorTimestamp = 0L,
            StartedUtcTicks = 1000L,
        });

        SqliteConnection.ClearAllPools();

        SessionCatalog.List(_sessionsDir).Select(s => s.Meta.SessionId).Should().Contain("catalog-check");
    }

    private static int PickFreePort() {
        System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
