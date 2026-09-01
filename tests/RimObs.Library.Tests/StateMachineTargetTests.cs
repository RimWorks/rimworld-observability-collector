using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using RimWorks.RimObs.Patching;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class StateMachineTargetTests {
    // regression: Concord refuses At.Transpiler on these (CONC123) and Harmony would patch the
    // stub that only builds the state machine, timing construction instead of the work.
    [Fact]
    public void FindsMoveNextBehindAnAsyncMethod() {
        MethodBase target = typeof(Targets).GetMethod(nameof(Targets.Async))!;

        StateMachineTarget.TryResolveMoveNext(target, out MethodInfo? moveNext).Should().BeTrue();

        moveNext!.Name.Should().Be("MoveNext");
        moveNext.DeclaringType!.Name.Should().Contain("Async");
    }

    [Fact]
    public void FindsMoveNextBehindAnIterator() {
        MethodBase target = typeof(Targets).GetMethod(nameof(Targets.Iterator))!;

        StateMachineTarget.TryResolveMoveNext(target, out MethodInfo? moveNext).Should().BeTrue();

        moveNext!.Name.Should().Be("MoveNext");
    }

    [Fact]
    public void LeavesAnOrdinaryMethodAlone() {
        MethodBase target = typeof(Targets).GetMethod(nameof(Targets.Plain))!;

        StateMachineTarget.TryResolveMoveNext(target, out MethodInfo? moveNext).Should().BeFalse();

        moveNext.Should().BeNull();
    }

    private static class Targets {
        public static async Task Async() => await Task.Yield();

        public static IEnumerable<int> Iterator() {
            yield return 1;
        }

        public static int Plain() => 1;
    }
}
