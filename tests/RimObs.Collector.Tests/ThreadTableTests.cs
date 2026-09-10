using System;
using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ThreadTableTests {
    [Fact]
    public void Records_a_registration() {
        ThreadTable table = new();
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [1],
            Names = ["Main"],
            Roles = [(int)ThreadRole.Main],
        });

        table.Snapshot().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadInfo(1, "Main", (int)ThreadRole.Main, 0L));
    }

    [Fact]
    public void An_empty_name_stays_empty_so_the_dashboard_can_fall_back() {
        ThreadTable table = new();
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [4],
            Names = [""],
            Roles = [(int)ThreadRole.UnityJob],
        });

        table.Snapshot().Should().ContainSingle().Which.Name.Should().BeEmpty();
    }

    [Fact]
    public void A_repeated_registration_does_not_duplicate_the_lane() {
        ThreadTable table = new();
        ThreadRegistrationsBatch batch = new() {
            ThreadIds = [1],
            Names = ["Main"],
            Roles = [(int)ThreadRole.Main],
        };
        table.Upsert(batch);
        table.Upsert(batch);

        table.Snapshot().Should().ContainSingle();
    }

    [Fact]
    public void A_repeated_registration_keeps_the_busy_total() {
        ThreadTable table = new();
        ThreadRegistrationsBatch batch = new() {
            ThreadIds = [1],
            Names = ["Main"],
            Roles = [(int)ThreadRole.Main],
        };
        table.Upsert(batch);
        table.AddBusy(1, 900L);
        table.Upsert(batch);

        table.Snapshot().Should().ContainSingle().Which.BusyTicks.Should().Be(900L);
    }

    [Fact]
    public void Busy_ticks_accumulate_per_lane() {
        ThreadTable table = new();
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [1, 2],
            Names = ["Main", ""],
            Roles = [(int)ThreadRole.Main, (int)ThreadRole.UnityJob],
        });
        table.AddBusy(2, 500L);
        table.AddBusy(2, 250L);

        table.Snapshot().Should().Contain(t => t.Id == 2 && t.BusyTicks == 750L);
    }

    [Fact]
    public void Busy_for_an_unknown_lane_is_ignored_rather_than_throwing() {
        ThreadTable table = new();

        Action act = () => table.AddBusy(99, 10L);

        act.Should().NotThrow();
        table.Snapshot().Should().BeEmpty();
    }
}
