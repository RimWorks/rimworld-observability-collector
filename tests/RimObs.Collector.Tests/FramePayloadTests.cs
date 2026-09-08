using System.Text.Json;
using RimWorks.RimObs.Collector.Aggregation;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class FramePayloadTests {
    [Fact]
    public void Map_publishes_per_node_alloc_bytes() {
        FrameSnapshot frame = new(
            CaptureOrdinal: 3,
            StartTicks: 100L,
            EndTicks: 300L,
            SectionIds: [1, 2],
            ParentIds: [-1, 1],
            NodeIds: [10, 11],
            ParentNodeIds: [-1, 10],
            NodeStartTicks: [100L, 150L],
            NodeElapsedTicks: [200L, 50L],
            NodeAllocBytes: [4096L, 128L]);

        using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(FramePayload.Map(frame, anchor: 0L, usPerTick: 1.0)));
        JsonElement alloc = doc.RootElement.GetProperty("nodes").GetProperty("alloc_bytes");

        alloc.GetArrayLength().Should().Be(2);
        alloc[0].GetInt64().Should().Be(4096);
        alloc[1].GetInt64().Should().Be(128);
    }

    [Fact]
    public void MapStats_puts_every_ring_statistic_under_its_own_name() {
        FrameRingStats stats = new(
            FrameCount: 7,
            MedianDurationTicks: 100L,
            P75DurationTicks: 150L,
            P90DurationTicks: 175L,
            P99DurationTicks: 200L,
            MinDurationTicks: 300L,
            MaxDurationTicks: 400L,
            NewestOrdinal: 55,
            OldestOrdinal: 11);

        using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(FramePayload.MapStats(stats, 0.1)));
        JsonElement root = doc.RootElement;

        root.GetProperty("frame_count").GetInt32().Should().Be(7);
        root.GetProperty("newest_ordinal").GetInt32().Should().Be(55);
        root.GetProperty("oldest_ordinal").GetInt32().Should().Be(11);
        root.GetProperty("median_us").GetDouble().Should().BeApproximately(10.0, 0.001);
        root.GetProperty("p75_us").GetDouble().Should().BeApproximately(15.0, 0.001);
        root.GetProperty("p90_us").GetDouble().Should().BeApproximately(17.5, 0.001);
        root.GetProperty("p99_us").GetDouble().Should().BeApproximately(20.0, 0.001);
        root.GetProperty("min_us").GetDouble().Should().BeApproximately(30.0, 0.001);
        root.GetProperty("max_us").GetDouble().Should().BeApproximately(40.0, 0.001);
    }
}
