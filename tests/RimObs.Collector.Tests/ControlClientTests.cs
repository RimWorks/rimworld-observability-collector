using RimWorks.RimObs.Collector.Instrumentation;
using RimWorks.RimObs.Collector.Tests.Stubs;
using RimWorks.RimObs.Wire.Control;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Collector.Tests;

public sealed class ControlClientTests {
    [Fact]
    public async Task Search_proxies_and_returns_decoded_response() {
        using StubControlServer stub = new("topsecret");
        stub.OnSearch = _ => new ControlSearchResponse {
            Results = [
                new ControlMethodDescriptor {
                    TypeFullName = "RimWorks.RimObs.Library.Tests.ResolverTargets",
                    MethodName = "Add",
                    Signature = "Int32 Add(Int32, Int32)",
                    ParamTypeFullNames = ["System.Int32", "System.Int32"],
                    AssemblyName = "RimObs.Library.Tests",
                },
            ],
        };
        stub.Start();
        ControlClient client = new(stub.Port, "topsecret");

        ControlSearchResponse res = await client.SearchAsync(new ControlSearchRequest { Query = "ResolverTargets", Limit = 5 });

        res.Results.Should().NotBeEmpty();
        res.Results.Should().Contain(r => r.MethodName == "Add");
    }

    [Fact]
    public async Task Search_throws_on_wrong_secret() {
        using StubControlServer stub = new("topsecret");
        stub.Start();
        ControlClient client = new(stub.Port, "wrong");

        Func<Task> act = () => client.SearchAsync(new ControlSearchRequest { Query = "ResolverTargets", Limit = 5 });

        ControlClientException ex = (await act.Should().ThrowAsync<ControlClientException>()).Which;
        ex.Status.Should().Be(401);
    }

    [Fact]
    public async Task Patch_proxies_and_returns_decoded_response() {
        using StubControlServer stub = new("topsecret");
        stub.OnPatch = _ => new ControlPatchResponse {
            PatchId = 42,
            SectionId = 7,
            SectionName = "MyMod.MyType.MyMethod",
            Status = PatchStatus.Active,
        };
        stub.Start();
        ControlClient client = new(stub.Port, "topsecret");

        ControlPatchResponse res = await client.PatchAsync(new ControlPatchRequest {
            TypeFullName = "MyMod.MyType",
            MethodName = "MyMethod",
            ParamTypeFullNames = [],
        });

        res.PatchId.Should().Be(42);
        res.SectionName.Should().Be("MyMod.MyType.MyMethod");
        res.Status.Should().Be(PatchStatus.Active);
    }

    [Fact]
    public async Task List_proxies_and_returns_decoded_response() {
        using StubControlServer stub = new("topsecret");
        stub.OnList = () => new ControlPatchListResponse {
            Patches = [
                new ControlPatchEntry {
                    PatchId = 1,
                    Signature = "MyMod.MyType.MyMethod()",
                    SectionId = 7,
                    Status = PatchStatus.Active,
                },
            ],
        };
        stub.Start();
        ControlClient client = new(stub.Port, "topsecret");

        ControlPatchListResponse res = await client.ListAsync();

        res.Patches.Should().HaveCount(1);
        res.Patches[0].PatchId.Should().Be(1);
        res.Patches[0].Signature.Should().Be("MyMod.MyType.MyMethod()");
    }

    [Fact]
    public async Task Unpatch_treats_not_found_as_idempotent_success() {
        using StubControlServer stub = new("topsecret");
        stub.OnUnpatch = _ => false;
        stub.Start();
        ControlClient client = new(stub.Port, "topsecret");

        Func<Task> act = () => client.UnpatchAsync(123);

        await act.Should().NotThrowAsync(
            "a 404 means the patch is already gone, which is the goal of an unpatch");
    }

    [Fact]
    public async Task Unpatch_throws_on_wrong_secret() {
        using StubControlServer stub = new("topsecret");
        stub.Start();
        ControlClient client = new(stub.Port, "wrong");

        Func<Task> act = () => client.UnpatchAsync(123);

        ControlClientException ex = (await act.Should().ThrowAsync<ControlClientException>()).Which;
        ex.Status.Should().Be(401);
    }
}
