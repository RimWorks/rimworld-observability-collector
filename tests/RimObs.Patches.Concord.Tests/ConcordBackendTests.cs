using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using RimWorks.RimObs.Patches.Concord;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class ConcordBackendTests : IDisposable {
    private readonly ConcordBackend _backend = new();
    private readonly RecordingSink _sink = new();

    public ConcordBackendTests() {
        PatchBackends.ResetForTests();
        Profiler.SetSink(_sink);
        Profiler.Enabled = true;
        Targets.Prefixes = 0;
        Targets.Postfixes = 0;
    }

    public void Dispose() {
        _backend.UnpatchAllForTests();
        Profiler.SetSink(null);
        PatchBackends.ResetForTests();
    }

    [Fact]
    public void NamesItself() {
        _backend.Name.Should().Be("Concord");
    }

    [Fact]
    public void RegistersItselfAtTheConcordPriority() {
        PatchBackends.Register(_backend, PatchBackends.ConcordPriority);
        PatchBackends.SelectBest();

        PatchBackends.Active!.Name.Should().Be("Concord");
    }

    [Fact]
    public void RecordsOneSampleAndKeepsTheReturnValue() {
        Patch(nameof(Targets.Add), "test.concord_add");

        int result = Targets.Add(7, 35);

        result.Should().Be(42);
        _sink.Samples.Should().HaveCount(1);
    }

    // regression: a section whose target throws must still emit its Stop. Harmony's finally
    // splicing is proven by MethodTransplanterTests; Concord's is not, and the whole plan rests
    // on the two behaving the same.
    [Fact]
    public void RecordsASampleEvenWhenTheTargetThrows() {
        Patch(nameof(Targets.ThrowsAlways), "test.concord_throws");

        Action act = Targets.ThrowsAlways;

        act.Should().Throw<InvalidOperationException>().WithMessage("nope");
        _sink.Samples.Should().HaveCount(1);
    }

    [Fact]
    public void UnpatchStopsRecording() {
        MethodInfo target = Patch(nameof(Targets.VoidNoOp), "test.concord_unpatch");
        Targets.VoidNoOp();
        _sink.Samples.Should().HaveCount(1);

        _backend.Unpatch(target);

        Targets.VoidNoOp();
        _sink.Samples.Should().HaveCount(1);
    }

    [Fact]
    public void RunsZeroArgHeadAndTailInjections() {
        MethodInfo prefixTarget = typeof(Targets).GetMethod(nameof(Targets.PrefixTarget))!;
        MethodInfo postfixTarget = typeof(Targets).GetMethod(nameof(Targets.PostfixTarget))!;
        _backend.PatchPrefix(prefixTarget, typeof(Targets).GetMethod(nameof(Targets.Prefix))!);
        _backend.PatchPostfix(postfixTarget, typeof(Targets).GetMethod(nameof(Targets.Postfix))!);

        Targets.PrefixTarget();
        Targets.PostfixTarget();

        Targets.Prefixes.Should().Be(1);
        Targets.Postfixes.Should().Be(1);
    }

    // regression: s_Handles used to key on the target alone, so Unpatch disposed whichever
    // injection went in last - a postfix, not the transpiler the contract names.
    [Fact]
    public void UnpatchRemovesOnlyTheTranspiler() {
        MethodInfo target = Patch(nameof(Targets.BothPatched), "test.concord_both");
        _backend.PatchPostfix(target, typeof(Targets).GetMethod(nameof(Targets.Postfix))!);
        Targets.BothPatched();
        _sink.Samples.Should().HaveCount(1);
        Targets.Postfixes.Should().Be(1);

        _backend.Unpatch(target);

        Targets.BothPatched();
        _sink.Samples.Should().HaveCount(1);
        Targets.Postfixes.Should().Be(2);
    }

    // several leaves converge on the one instruction that now also carries HandlerEnd, which is
    // the shape the Concord-specific EndExceptionBlock placement is most likely to get wrong.
    [Fact]
    public void BranchyMethodWithMultipleReturnsStillRecordsOnce() {
        Patch(nameof(Targets.MultipleReturns), "test.concord_multi_returns");

        Targets.MultipleReturns(0).Should().Be("zero");
        Targets.MultipleReturns(1).Should().Be("one");
        Targets.MultipleReturns(2).Should().Be("two");
        Targets.MultipleReturns(99).Should().Be("other");

        _sink.Samples.Should().HaveCount(4);
    }

    [Fact]
    public void ReportsNoConflicts() {
        _backend.ConflictsFor(typeof(Targets).GetMethod(nameof(Targets.VoidNoOp))!).Should().BeEmpty();
    }

    [Fact]
    public void UnregisteredMethodRecordsNothing() {
        MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.Unregistered))!;
        _backend.Patch(target);

        Targets.Unregistered();

        _sink.Samples.Should().BeEmpty();
    }

    private MethodInfo Patch(string methodName, string sectionName) {
        MethodInfo target = typeof(Targets).GetMethod(methodName)!;
        SectionCatalog.RegisterDirect(sectionName, target);
        _backend.Patch(target);
        return target;
    }

    public static class Targets {
        public static int Prefixes;
        public static int Postfixes;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void VoidNoOp() {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PrefixTarget() {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PostfixTarget() {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void BothPatched() {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Unregistered() {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string MultipleReturns(int x) {
            if (x == 0)
                return "zero";
            if (x == 1)
                return "one";
            if (x == 2)
                return "two";
            return "other";
        }

        public static void Prefix() => Prefixes++;

        public static void Postfix() => Postfixes++;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Add(int a, int b) {
            return a + b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowsAlways() {
            throw new InvalidOperationException("nope");
        }
    }
}
