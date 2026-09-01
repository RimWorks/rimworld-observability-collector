using FluentAssertions;
using RimWorks.RimObs.Patching;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class PatchConflictTests {
    [Fact]
    public void CarriesOwnerKindAndPriority() {
        PatchConflict conflict = new(
            sectionName: "some.section",
            targetMethod: "SomeType.SomeMethod",
            otherOwner: "other.mod",
            patchType: PatchKind.Transpiler,
            priority: 400,
            patchMethod: "OtherType.OtherMethod"
        );

        conflict.OtherOwner.Should().Be("other.mod");
        conflict.PatchType.Should().Be(PatchKind.Transpiler);
        conflict.Priority.Should().Be(400);
    }

    // PatchConflictRecorder casts these straight to the wire byte and the dashboard decodes by
    // index, so renumbering the enum silently relabels every conflict row.
    [Theory]
    [InlineData(PatchKind.Prefix, 1)]
    [InlineData(PatchKind.Postfix, 2)]
    [InlineData(PatchKind.Transpiler, 3)]
    [InlineData(PatchKind.Finalizer, 4)]
    public void KindMatchesTheWireByteTheDashboardDecodes(PatchKind kind, int expected) {
        ((int)kind).Should().Be(expected);
    }
}
