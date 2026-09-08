using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Storage;
using RimWorks.RimObs.Collector.Tests.Stubs;
using RimWorks.RimObs.Wire;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public class DynamicPatchReplayServiceTests {
    private static DynamicPatchReplayService Build(DynamicPatchStore store, SessionMetaRegistry registry) =>
        new(registry, store, NullLogger<DynamicPatchReplayService>.Instance);

    private static SessionMetaRegistry RegistryFor(StubControlServer stub, string sessionId = "s1") {
        SessionMetaRegistry registry = new();
        registry.OnSessionMeta(new SessionMeta {
            SessionId = sessionId,
            ControlPort = stub.Port,
            ControlSecret = "s",
        });
        return registry;
    }

    [Fact]
    public async Task Sweep_does_nothing_until_a_session_reports_a_control_port() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long row = store.Insert("X.Y", "Z", "");

        await Build(store, new SessionMetaRegistry()).SweepAsync();

        store.Find(row)!.LastStatus.Should().Be(PatchStatus.Pending);
    }

    [Fact]
    public async Task Sweep_reapplies_the_stored_patches_when_the_session_appears() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long row = store.Insert("X.Y", "Z", "");
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => new ControlPatchResponse { PatchId = 6, SectionId = 2, Status = PatchStatus.Active };
        stub.Start();

        await Build(store, RegistryFor(stub)).SweepAsync();

        DynamicPatchRow applied = store.Find(row)!;
        applied.LastStatus.Should().Be(PatchStatus.Active);
        applied.LivePatchId.Should().Be(6);
    }

    // the game renumbers its patch ids on restart, so a new session must drop the old ones.
    [Fact]
    public async Task Sweep_forgets_live_ids_from_the_previous_session() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long row = store.Insert("X.Y", "Gone", "");
        store.UpdateLivePatchId(row, 9);
        store.UpdateStatus(row, PatchStatus.Active, null);
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => new ControlPatchResponse { Status = PatchStatus.Stale, ErrorReason = "method not found" };
        stub.Start();

        await Build(store, RegistryFor(stub, "session-two")).SweepAsync();

        DynamicPatchRow after = store.Find(row)!;
        after.LivePatchId.Should().BeNull();
        after.LastStatus.Should().Be(PatchStatus.Stale);
    }

    // no map is loaded yet, so the game cannot drain its queue. the row must stay retryable.
    [Fact]
    public async Task A_drain_timeout_leaves_the_row_pending_and_a_later_sweep_lands_it() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        long row = store.Insert("X.Y", "Z", "");
        using StubControlServer stub = new("s");
        stub.PatchHttpStatus = 504;
        stub.FailureReason = "main-thread drain timed out";
        stub.Start();
        SessionMetaRegistry registry = RegistryFor(stub);
        DynamicPatchReplayService service = Build(store, registry);

        await service.SweepAsync();
        store.Find(row)!.LastStatus.Should().Be(PatchStatus.Pending);

        stub.PatchHttpStatus = null;
        stub.OnPatch = _ => new ControlPatchResponse { PatchId = 3, SectionId = 1, Status = PatchStatus.Active };
        await service.SweepAsync();

        DynamicPatchRow after = store.Find(row)!;
        after.LastStatus.Should().Be(PatchStatus.Active);
        after.LivePatchId.Should().Be(3);
    }

    [Fact]
    public async Task A_settled_session_stops_calling_the_game() {
        using DynamicPatchStore store = DynamicPatchStore.OpenInMemory();
        store.Insert("X.Y", "Z", "");
        int patchCalls = 0;
        using StubControlServer stub = new("s");
        stub.OnPatch = _ => {
            patchCalls++;
            return new ControlPatchResponse { PatchId = 1, SectionId = 1, Status = PatchStatus.Active };
        };
        stub.Start();
        DynamicPatchReplayService service = Build(store, RegistryFor(stub));

        await service.SweepAsync();
        await service.SweepAsync();
        await service.SweepAsync();

        patchCalls.Should().Be(1);
    }
}
