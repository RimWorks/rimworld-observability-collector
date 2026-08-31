using System.Collections.Generic;
using RimWorks.RimObs.Collector.Bundle;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class BundleSizeEstimatorTests {
    [Fact]
    public void Estimate_BaselineFloor_ForRequiredOnly() {
        BundleSizeEstimate estimate = BundleSizeEstimator.Estimate(new BundleEstimateInput {
            SectionCount = 0,
            MetricCount = 0,
            AllocationRowCount = 0,
            CallEdgeCount = 0,
            GcEventCount = 0,
            PatchConflictCount = 0,
            MetricsSqliteBytes = 0,
            Includes = new HashSet<BundleContentKey>(),
        });

        estimate.TotalBytes.Should().BeGreaterThan(0).And.BeLessThan(64 * 1024);
    }

    [Fact]
    public void Estimate_GrowsWithRows() {
        BundleSizeEstimate small = BundleSizeEstimator.Estimate(new BundleEstimateInput {
            AllocationRowCount = 100,
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Allocations },
        });
        BundleSizeEstimate large = BundleSizeEstimator.Estimate(new BundleEstimateInput {
            AllocationRowCount = 10_000,
            Includes = new HashSet<BundleContentKey> { BundleContentKey.Allocations },
        });

        large.TotalBytes.Should().BeGreaterThan(small.TotalBytes * 50);
    }

    [Fact]
    public void Estimate_IncludesSqliteBytesWhenRequested() {
        BundleSizeEstimate with = BundleSizeEstimator.Estimate(new BundleEstimateInput {
            MetricsSqliteBytes = 5_000_000,
            Includes = new HashSet<BundleContentKey> { BundleContentKey.MetricsSqlite },
        });
        BundleSizeEstimate without = BundleSizeEstimator.Estimate(new BundleEstimateInput {
            MetricsSqliteBytes = 5_000_000,
            Includes = new HashSet<BundleContentKey>(),
        });

        (with.TotalBytes - without.TotalBytes).Should().BeGreaterOrEqualTo(4_000_000);
    }

    [Fact]
    public void Estimate_ExceedsSoftCap_TrueWhenOver25Mb() {
        BundleSizeEstimate estimate = BundleSizeEstimator.Estimate(new BundleEstimateInput {
            MetricsSqliteBytes = 50_000_000,
            Includes = new HashSet<BundleContentKey> { BundleContentKey.MetricsSqlite },
        });

        estimate.ExceedsSoftCap.Should().BeTrue();
        BundleSizeEstimator.SoftCapBytes.Should().Be(25 * 1024 * 1024);
    }
}
