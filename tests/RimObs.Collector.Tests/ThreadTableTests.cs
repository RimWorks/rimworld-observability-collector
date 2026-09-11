using RimWorks.RimObs.Collector.Aggregation;
using RimWorks.RimObs.Wire;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ThreadTableTests {
    [Fact]
    public void MainLaneId_trusts_only_registered_main_lanes() {
        ThreadTable table = new();
        table.MainLaneId().Should().Be(0);

        // a busy sample beats the registration and fabricates a role-0 placeholder.
        table.AddBusy(7, 100L);
        table.MainLaneId().Should().Be(0);

        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [9, 1],
            Names = ["", "Main"],
            Roles = [(int)ThreadRole.Main, (int)ThreadRole.Main],
        });
        table.MainLaneId().Should().Be(1);
    }

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
    public void A_recycled_id_under_a_new_name_starts_its_busy_time_at_zero() {
        ThreadTable table = new();
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [7],
            Names = ["Worker A"],
            Roles = [(int)ThreadRole.UnityJob],
        });
        table.AddBusy(7, 900L);
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [7],
            Names = ["Worker B"],
            Roles = [(int)ThreadRole.UnityJob],
        });

        table.Snapshot().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadInfo(7, "Worker B", (int)ThreadRole.UnityJob, 0L));
    }

    [Fact]
    public void A_recycled_id_under_a_new_role_starts_its_busy_time_at_zero() {
        ThreadTable table = new();
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [7],
            Names = ["Worker"],
            Roles = [(int)ThreadRole.UnityJob],
        });
        table.AddBusy(7, 900L);
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [7],
            Names = ["Worker"],
            Roles = [(int)ThreadRole.Mod],
        });

        table.Snapshot().Should().ContainSingle().Which.BusyTicks.Should().Be(0L);
    }

    [Fact]
    public void A_recycled_id_that_registered_unnamed_starts_its_busy_time_at_zero() {
        ThreadTable table = new();
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [7],
            Names = [""],
            Roles = [(int)ThreadRole.UnityJob],
        });
        table.AddBusy(7, 900L);
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [7],
            Names = ["MyMod"],
            Roles = [(int)ThreadRole.Mod],
        });

        table.Snapshot().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadInfo(7, "MyMod", (int)ThreadRole.Mod, 0L));
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
    public void Busy_for_an_unknown_lane_creates_it_so_a_lost_registration_only_costs_the_name() {
        ThreadTable table = new();

        table.AddBusy(99, 10L);

        table.Snapshot().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadInfo(99, "", 0, 10L));
    }

    [Fact]
    public void A_late_registration_fills_in_the_name_and_keeps_the_busy_total() {
        ThreadTable table = new();
        table.AddBusy(99, 10L);
        table.Upsert(new ThreadRegistrationsBatch {
            ThreadIds = [99],
            Names = ["Worker"],
            Roles = [(int)ThreadRole.UnityJob],
        });

        table.Snapshot().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ThreadInfo(99, "Worker", (int)ThreadRole.UnityJob, 10L));
    }
}
