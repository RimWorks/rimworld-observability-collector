using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Collector.Tests.Stubs;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class DynamicPatchReplayerTests {
    [Fact]
    public async Task Replay_marks_active_when_proxy_succeeds() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        store.Insert("X.Y", "Z", "");
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => new ControlPatchResponse {
            PatchId = 1,
            SectionId = 0,
            SectionName = "test.dynamic.X.Y:Z",
            Status = PatchStatus.Active,
        };
        stub.Start();
        ControlClient client = new ControlClient(stub.Port, "s");

        DynamicPatchReplayer replayer = new DynamicPatchReplayer(store);
        await replayer.ReplayAsync(client);

        store.List()[0].LastStatus.Should().Be(PatchStatus.Active);
    }

    // the library hands back its own id, and the store row id is not it. without recording
    // this, an unpatch targets whatever live patch happens to share the row number.
    [Fact]
    public async Task Replay_records_the_live_patch_id_the_library_returned() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long rowId = store.Insert("X.Y", "Z", "");
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => new ControlPatchResponse {
            PatchId = 9,
            SectionId = 3,
            SectionName = "test.dynamic.X.Y:Z",
            Status = PatchStatus.Active,
        };
        stub.Start();

        await new DynamicPatchReplayer(store).ReplayAsync(new ControlClient(stub.Port, "s"));

        store.Find(rowId)!.LivePatchId.Should().Be(9);
    }

    [Fact]
    public async Task Replay_clears_a_stale_live_id_when_the_patch_no_longer_applies() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long rowId = store.Insert("X.Y", "Gone", "");
        store.UpdateLivePatchId(rowId, 4);
        new DynamicPatchReplayer(store).ForgetLiveIds();
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => new ControlPatchResponse {
            Status = PatchStatus.Stale,
            ErrorReason = "method not found",
        };
        stub.Start();

        await new DynamicPatchReplayer(store).ReplayAsync(new ControlClient(stub.Port, "s"));

        DynamicPatchRow row = store.Find(rowId)!;
        row.LastStatus.Should().Be(PatchStatus.Stale);
        row.LivePatchId.Should().BeNull();
    }

    [Fact]
    public async Task Replay_marks_stale_when_proxy_returns_4xx() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        store.Insert("X.Y", "MissingMethod", "");
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => throw new System.NotImplementedException();
        stub.Start();
        ControlClient client = new ControlClient(stub.Port, "s");

        DynamicPatchReplayer replayer = new DynamicPatchReplayer(store);
        await replayer.ReplayAsync(client);

        store.List()[0].LastStatus.Should().Be(PatchStatus.Stale);
    }
}
