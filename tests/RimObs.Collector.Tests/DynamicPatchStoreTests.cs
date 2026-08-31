using System;
using System.IO;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class DynamicPatchStoreTests {
    private static DynamicPatchStore Open() => DynamicPatchStore.OpenInMemory();

    [Fact]
    public void Insert_then_list_returns_row() {
        using DynamicPatchStore store = Open();
        long id = store.Insert("Verse.PathFinder", "FindPath", "Verse.IntVec3;Verse.IntVec3");

        IReadOnlyList<DynamicPatchRow> rows = store.List();

        rows.Should().HaveCount(1);
        rows[0].Id.Should().Be(id);
        rows[0].TypeFullName.Should().Be("Verse.PathFinder");
        rows[0].LastStatus.Should().Be(PatchStatus.Pending);
    }

    [Fact]
    public void Insert_is_idempotent_on_signature_triple() {
        using DynamicPatchStore store = Open();
        long first = store.Insert("A", "B", "P");
        long second = store.Insert("A", "B", "P");
        second.Should().Be(first);
        store.List().Should().HaveCount(1);
    }

    [Fact]
    public void Update_status_persists() {
        using DynamicPatchStore store = Open();
        long id = store.Insert("A", "B", "");
        store.UpdateStatus(id, PatchStatus.Stale, "mod uninstalled");

        DynamicPatchRow row = store.List()[0];
        row.LastStatus.Should().Be(PatchStatus.Stale);
        row.LastError.Should().Be("mod uninstalled");
    }

    [Theory]
    [InlineData(PatchStatus.Pending)]
    [InlineData(PatchStatus.Active)]
    [InlineData(PatchStatus.Refused)]
    [InlineData(PatchStatus.Stale)]
    public void Update_status_round_trips_every_status(PatchStatus status) {
        using DynamicPatchStore store = Open();
        long id = store.Insert("A", "B", "");
        store.UpdateStatus(id, status, null);

        store.List()[0].LastStatus.Should().Be(status);
    }

    [Fact]
    public void Delete_removes_row() {
        using DynamicPatchStore store = Open();
        long id = store.Insert("A", "B", "");
        store.Delete(id).Should().BeTrue();
        store.List().Should().BeEmpty();
    }

    [Fact]
    public void Open_enables_wal_so_overlapping_collectors_dont_crash_on_locked_db() {
        string path = Path.Combine(Path.GetTempPath(), $"rimobs-patch-{Guid.NewGuid():N}.db");
        try {
            using (DynamicPatchStore store = DynamicPatchStore.Open(path)) {
                store.Insert("Verse.PathFinder", "FindPath", "Verse.IntVec3");
                File.Exists(path + "-wal").Should().BeTrue(
                    "WAL leaves a -wal sidecar after the first write; without it a second collector hits a locked DB at startup");
            }
        }
        finally {
            Cleanup(path);
        }
    }

    [Fact]
    public void Two_stores_on_same_file_share_state_without_throwing() {
        string path = Path.Combine(Path.GetTempPath(), $"rimobs-patch-{Guid.NewGuid():N}.db");
        try {
            using DynamicPatchStore first = DynamicPatchStore.Open(path);
            using DynamicPatchStore second = DynamicPatchStore.Open(path);

            first.Insert("First.Type", "M", "P1");
            second.Insert("Second.Type", "M", "P2");

            second.List().Should().HaveCount(2);
        }
        finally {
            Cleanup(path);
        }
    }

    private static void Cleanup(string path) {
        SqliteConnection.ClearAllPools();
        foreach (string sidecar in new[] { path, path + "-wal", path + "-shm" }) {
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }
    }
}
