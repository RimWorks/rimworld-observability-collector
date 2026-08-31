using RimWorks.RimObs.Collector.Aggregation;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class SectionDistributionTests {
    [Fact]
    public void Empty_distribution_returns_empty_snapshot() {
        SectionDistribution dist = new();

        PercentileSnapshot snap = dist.SnapshotPercentiles();

        snap.Should().Be(PercentileSnapshot.Empty);
    }

    [Fact]
    public void Percentiles_track_recorded_distribution() {
        SectionDistribution dist = new();
        long epoch = 1_700_000_000;
        for (long v = 1; v <= 1000; v++) {
            dist.Record(epoch, v);
        }

        PercentileSnapshot snap = dist.SnapshotPercentiles();

        snap.P50Ticks.Should().BeInRange(490, 510);
        snap.P95Ticks.Should().BeInRange(940, 960);
        snap.P99Ticks.Should().BeInRange(985, 1000);
    }

    [Fact]
    public void Percentiles_are_monotonic() {
        SectionDistribution dist = new();
        long epoch = 1_700_000_000;
        for (long v = 10; v <= 10_000; v += 10) {
            dist.Record(epoch, v);
        }

        PercentileSnapshot snap = dist.SnapshotPercentiles();

        snap.P50Ticks.Should().BeLessOrEqualTo(snap.P95Ticks);
        snap.P95Ticks.Should().BeLessOrEqualTo(snap.P99Ticks);
    }

    [Fact]
    public void Below_lowest_values_are_clamped_to_lowest() {
        SectionDistribution dist = new();

        dist.Record(1_700_000_000, -100);
        dist.Record(1_700_000_000, 0);

        PercentileSnapshot snap = dist.SnapshotPercentiles();
        snap.P50Ticks.Should().Be(1);
        snap.P99Ticks.Should().Be(1);
    }

    [Fact]
    public void Above_highest_values_are_clamped_so_record_does_not_throw() {
        SectionDistribution dist = new();
        Action act = () => dist.Record(1_700_000_000, 100_000_000_000);
        act.Should().NotThrow();
    }

    [Fact]
    public void Timeline_records_one_bucket_per_second() {
        SectionDistribution dist = new();
        long epoch = 1_700_000_000;

        dist.Record(epoch, 100);
        dist.Record(epoch, 200);
        dist.Record(epoch + 1, 50);

        TimelineBucket[] points = dist.SnapshotTimeline(epoch + 1);

        points.Should().HaveCount(2);
        points[0].EpochSeconds.Should().Be(epoch);
        points[0].Count.Should().Be(2);
        points[0].TotalTicks.Should().Be(300);
        points[1].EpochSeconds.Should().Be(epoch + 1);
        points[1].Count.Should().Be(1);
        points[1].TotalTicks.Should().Be(50);
    }

    [Fact]
    public void Timeline_returns_points_sorted_by_epoch() {
        SectionDistribution dist = new();
        long epoch = 1_700_000_000;

        dist.Record(epoch + 5, 5);
        dist.Record(epoch + 2, 2);
        dist.Record(epoch + 10, 10);
        dist.Record(epoch, 1);

        TimelineBucket[] points = dist.SnapshotTimeline(epoch + 10);

        points.Select(p => p.EpochSeconds).Should().BeInAscendingOrder();
        points.Should().HaveCount(4);
    }

    [Fact]
    public void Timeline_drops_buckets_older_than_window() {
        SectionDistribution dist = new();
        long epoch = 1_700_000_000;

        dist.Record(epoch, 1);
        dist.Record(epoch + SectionDistribution.BucketCount, 2);

        TimelineBucket[] points = dist.SnapshotTimeline(epoch + SectionDistribution.BucketCount);

        points.Should().HaveCount(1);
        points[0].EpochSeconds.Should().Be(epoch + SectionDistribution.BucketCount);
        points[0].TotalTicks.Should().Be(2);
    }

    [Fact]
    public void Timeline_reuses_bucket_slot_when_epoch_wraps_capacity() {
        SectionDistribution dist = new();
        long epoch = 1_700_000_000;
        long wrapped = epoch + SectionDistribution.BucketCount;

        dist.Record(epoch, 999);
        dist.Record(wrapped, 7);

        TimelineBucket[] points = dist.SnapshotTimeline(wrapped);

        points.Should().HaveCount(1);
        points[0].EpochSeconds.Should().Be(wrapped);
        points[0].Count.Should().Be(1);
        points[0].TotalTicks.Should().Be(7);
    }
}
