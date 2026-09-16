using System;
using RimWorks.RimObs.Observers;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class VramCensusTests {
    [Fact]
    public void Totals_split_by_kind_and_count_every_entry() {
        VramCensus census = new(topCount: 4);
        census.Add(VramCensus.KindTexture, 100, "a");
        census.Add(VramCensus.KindTexture, 50, "b");
        census.Add(VramCensus.KindMesh, 30, "c");
        census.Add(VramCensus.KindRenderTarget, 20, "d");

        VramBatch batch = census.ToBatch(driverBytes: 999);

        batch.DriverBytes.Should().Be(999);
        batch.TextureBytes.Should().Be(150);
        batch.TextureCount.Should().Be(2);
        batch.MeshBytes.Should().Be(30);
        batch.MeshCount.Should().Be(1);
        batch.RenderTargetBytes.Should().Be(20);
        batch.RenderTargetCount.Should().Be(1);
    }

    [Fact]
    public void The_top_table_keeps_the_biggest_and_sorts_descending() {
        VramCensus census = new(topCount: 3);
        census.Add(VramCensus.KindTexture, 10, "small");
        census.Add(VramCensus.KindTexture, 500, "big");
        census.Add(VramCensus.KindMesh, 200, "mid");
        census.Add(VramCensus.KindTexture, 300, "evicts-small");

        VramBatch batch = census.ToBatch(0);

        batch.TopNames.Should().Equal("big", "evicts-small", "mid");
        batch.TopBytes.Should().Equal(500, 300, 200);
        batch.TopKinds.Should().Equal(
            VramCensus.KindTexture, VramCensus.KindTexture, VramCensus.KindMesh);
    }

    // the scan sizes tens of thousands of objects; a name per object would be an allocation
    // storm, so callers ask Qualifies first and pass null for everything that cannot place.
    [Fact]
    public void Qualifies_gates_names_and_a_null_name_still_counts_toward_totals() {
        VramCensus census = new(topCount: 2);
        census.Qualifies(100).Should().BeTrue();
        census.Add(VramCensus.KindTexture, 100, "a");
        census.Add(VramCensus.KindTexture, 200, "b");

        census.Qualifies(1).Should().BeFalse();
        census.Add(VramCensus.KindTexture, 1, null);

        VramBatch batch = census.ToBatch(0);
        batch.TextureCount.Should().Be(3);
        batch.TextureBytes.Should().Be(301);
        batch.TopNames.Should().Equal("b", "a");
    }
}
