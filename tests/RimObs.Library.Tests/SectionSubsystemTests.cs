using System.Reflection;
using RimWorks.RimObs.Patching;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

// dynamic sections used to register with no subsystem, so every auto-instrumented bar drew
// untagged. the classifier buckets them by name, with engine as the catch-all.
public sealed class SectionSubsystemTests {
    public static class TickManagerFake {
        public static void DoSingleTick() { }
    }

    public static class PawnRendererFake {
        public static void DynamicDrawPhaseAt() { }
    }

    public static class MainTabWindowFake {
        public static void DoWindowContents() { }
    }

    public static class JobDriverFake {
        public static void TryMakePreToilReservations() { }
    }

    public static class GameFake {
        public static void UpdatePlay() { }
    }

    private static MethodBase M(System.Type type) => type.GetMethods()[0];

    [Theory]
    [InlineData(typeof(TickManagerFake), "tick")]
    [InlineData(typeof(PawnRendererFake), "render")]
    [InlineData(typeof(MainTabWindowFake), "ui")]
    [InlineData(typeof(JobDriverFake), "ai")]
    [InlineData(typeof(GameFake), "engine")]
    public void Buckets_a_method_by_its_names(System.Type type, string expected) {
        SectionCatalog.InferSubsystem(M(type)).Should().Be(expected);
    }
}
