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
        long id = store.Insert("X.Y", "Z", "");
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

    [Fact]
    public async Task Replay_marks_stale_when_proxy_returns_4xx() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long id = store.Insert("X.Y", "MissingMethod", "");
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => throw new System.NotImplementedException();
        stub.Start();
        ControlClient client = new ControlClient(stub.Port, "s");

        DynamicPatchReplayer replayer = new DynamicPatchReplayer(store);
        await replayer.ReplayAsync(client);

        store.List()[0].LastStatus.Should().Be(PatchStatus.Stale);
    }
}
