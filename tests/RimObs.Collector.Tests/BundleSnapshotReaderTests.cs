using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Collector.Bundle;
using RimWorks.RimObs.Collector.Comparison;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleSnapshotReaderTests : IDisposable {
    private readonly string _importsDir;

    public BundleSnapshotReaderTests() {
        _importsDir = Path.Combine(Path.GetTempPath(), "rimobs-bundlesnap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_importsDir);
    }

    public void Dispose() {
        try {
            Directory.Delete(_importsDir, recursive: true);
        }
        catch (IOException) {
        }
        GC.SuppressFinalize(this);
    }

    private static SessionAggregator BuildSeededAggregator() {
        SessionAggregator aggregator = new SessionAggregator();
        aggregator.OnSessionMeta(new SessionMeta {
            SessionId = "bundle-sess",
            StartedUtcTicks = new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc).Ticks,
            StopwatchFrequency = 10_000_000,
            LibraryVersion = "0.1.0",
            GameVersion = "1.5",
        });
        aggregator.OnSectionRegistrations(new SectionRegistrationsBatch {
            SectionIds = [1, 2],
            Names = ["modA_scan", "modB_draw"],
        });
        aggregator.OnSectionBatch(new SectionBatch {
            SectionIds = [1, 1, 2],
            StartTimestamps = [100, 200, 300],
            ElapsedTicks = [50, 30, 100],
        });
        aggregator.OnMetricRegistrations(new MetricRegistrationsBatch {
            MetricIds = [10],
            Names = ["modA_frames"],
            Kinds = [0],
            Units = ["count"],
        });
        aggregator.OnMetrics(new MetricsBatch {
            MetricIds = [10],
            LabelCanonicals = ["scene=map"],
            Kinds = [0],
            Values = [42],
            SampleCounts = [7],
        });
        return aggregator;
    }

    private async Task<(BundleImportRegistry Registry, string Token)> ExportAndImport() {
        BundleExportService export = new BundleExportService(BuildSeededAggregator(), collectorVersion: "0.1.0");
        BundleExportResult result = await export.ExportAsync(new BundleExportRequest {
            SessionId = "bundle-sess",
            Includes = new System.Collections.Generic.HashSet<BundleContentKey>(),
            Force = false,
        }, CancellationToken.None);
        result.Status.Should().Be(BundleExportStatus.Ok);

        BundleImportRegistry registry = new BundleImportRegistry(_importsDir, TimeSpan.FromMinutes(30));
        BundleImportService importer = new BundleImportService(registry);
        using MemoryStream archive = new MemoryStream(result.Bytes!);
        BundleImportResult import = await importer.ImportAsync(archive);
        import.Status.Should().Be(BundleImportStatus.Ok);
        return (registry, import.Entry!.Token);
    }

    [Fact]
    public async Task Read_round_trips_session_meta_from_exported_bundle() {
        (BundleImportRegistry registry, string token) = await ExportAndImport();
        BundleSnapshotReader reader = new BundleSnapshotReader(registry);

        SessionSnapshot? snapshot = reader.Read(token);

        snapshot.Should().NotBeNull();
        snapshot!.SessionId.Should().Be("bundle-sess");
        snapshot.IsCurrent.Should().BeFalse();
        snapshot.LibraryVersion.Should().Be("0.1.0");
        snapshot.GameVersion.Should().Be("1.5");
        snapshot.StartedUtcTicks.Should().Be(new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc).Ticks);
    }

    [Fact]
    public async Task Read_round_trips_section_totals_in_nanoseconds() {
        (BundleImportRegistry registry, string token) = await ExportAndImport();
        BundleSnapshotReader reader = new BundleSnapshotReader(registry);

        SessionSnapshot snapshot = reader.Read(token)!;

        snapshot.Sections.Should().HaveCount(2);
        SectionSnapshot scan = snapshot.Sections.Single(s => s.Name == "modA_scan");
        scan.SampleCount.Should().Be(2);
        scan.TotalNs.Should().Be(8000);
        scan.Owner.Should().Be("modA");
        SectionSnapshot draw = snapshot.Sections.Single(s => s.Name == "modB_draw");
        draw.TotalNs.Should().Be(10000);
    }

    [Fact]
    public async Task Read_round_trips_metric_label_totals() {
        (BundleImportRegistry registry, string token) = await ExportAndImport();
        BundleSnapshotReader reader = new BundleSnapshotReader(registry);

        SessionSnapshot snapshot = reader.Read(token)!;

        MetricSnapshot metric = snapshot.Metrics.Single(m => m.Name == "modA_frames");
        metric.Value.Should().Be(42);
        metric.TotalSampleCount.Should().Be(7);
        metric.Unit.Should().Be("count");
    }

    [Fact]
    public void Read_returns_null_for_unknown_token() {
        BundleImportRegistry registry = new BundleImportRegistry(_importsDir, TimeSpan.FromMinutes(30));
        BundleSnapshotReader reader = new BundleSnapshotReader(registry);

        reader.Read("not-a-real-token").Should().BeNull();
    }

    [Fact]
    public async Task Exported_bundle_compares_against_a_session_snapshot() {
        (BundleImportRegistry registry, string token) = await ExportAndImport();
        SessionSnapshot baseline = new BundleSnapshotReader(registry).Read(token)!;

        SessionSnapshot head = baseline with {
            SessionId = "head",
            Sections = baseline.Sections
                .Select(s => s with { TotalNs = s.TotalNs * 2 })
                .ToList(),
        };

        ComparisonResult result = SessionComparer.Compare(baseline, head);

        result.Timing.HeadTotalNs.Should().Be(result.Timing.BaseTotalNs * 2);
        result.Timing.DeltaNs.Should().Be(result.Timing.BaseTotalNs);
    }
}
