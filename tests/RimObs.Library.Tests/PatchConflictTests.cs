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
}
