using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using RimObsTest.AutoFixtures;
using RimWorks.RimObs.Auto;
using RimWorks.RimObs.Library.Control;
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
        AutoInstrumentRunner.ResetForTests();
        AutoMute.ResetForTests();
        PatchRegistry.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        PatchBackends.ResetForTests();
    }

    // auto-instrumentation drives IPatchBackend.Patch, so it has to be proven against Concord
    // too. these live here because a second Concord test class breaks the suite, see below.
    [Fact]
    public void AutoInstrumentationPatchesAFilteredMethodAndRecordsIt() {
        ApplyAutoFilter("RimObsTest.AutoFixtures.AutoTargets::Worthwhile");
        Drain();

        AutoTargets.Worthwhile(4);

        AutoInstrumentRunner.Instrumented.Should().Be(1);
        AutoInstrumentRunner.Refused.Should().Be(0);
        _sink.Samples.Should().HaveCount(1);
    }

    // one pump is one frame's budget, and a Concord patch can cost more than the whole budget,
    // so a four-target plan takes several frames.
    [Fact]
    public void AutoInstrumentationPatchesEveryEligibleMethodOverSeveralPumps() {
        ApplyAutoFilter("RimObsTest.AutoFixtures.*");
        Drain();

        AutoTargets.Worthwhile(2);
        AutoTargets.AlsoWorthwhile(2);
        AutoTargets.ThirdWorthwhile(2);
        OtherAutoTargets.Elsewhere(2);

        AutoInstrumentRunner.Refused.Should().Be(0);
        _sink.Samples.Should().HaveCount(4);
    }

    // Concord keys its handles on (target, At), so a second transpiler on one method loses the
    // first handle. the scanner has to keep an already-owned method out of the plan.
    [Fact]
    public void AutoInstrumentationSkipsAMethodAnExistingSectionAlreadyOwns() {
        MethodInfo owned = typeof(AutoTargets).GetMethod(nameof(AutoTargets.Worthwhile))!;
        SectionCatalog.RegisterDirect("test.concord_auto_owned", owned);
        _backend.Patch(owned);

        ApplyAutoFilter("RimObsTest.AutoFixtures.AutoTargets");
        Drain();
        AutoTargets.Worthwhile(2);

        _sink.Samples.Should().HaveCount(1);
    }

    private static void Drain() {
        while (AutoInstrumentRunner.Pending > 0)
            AutoInstrumentRunner.Pump();
        AutoInstrumentRunner.Pump();
    }

    private void ApplyAutoFilter(string filters) {
        PatchBackends.Register(_backend, PatchBackends.ConcordPriority);
        PatchBackends.SelectBest(scan: false);
        // muting off: these prove patching mechanics, and pending sections hold their samples.
        AutoInstrumentRunner.ApplyFilters(
            filters, ignore: null, autoMute: false, "test.owner", [typeof(AutoTargets).Assembly]);
    }

    [Fact]
    public void NamesItself() {
        _backend.Name.Should().Be("Concord");
    }

    // Concord has no GetPatchInfo equivalent, so an empty list must not be read as "no conflicts".
    [Fact]
    public void DoesNotClaimToReportConflicts() {
        _backend.SupportsConflictReporting.Should().BeFalse();
        _backend.ConflictsFor(typeof(Targets).GetMethod(nameof(Targets.VoidNoOp))!).Should().BeEmpty();
    }

    [Fact]
    public void RegistersItselfAtTheConcordPriority() {
        PatchBackends.Register(_backend, PatchBackends.ConcordPriority);
        PatchBackends.SelectBest(scan: false);

        PatchBackends.Active!.Name.Should().Be("Concord");
    }

    [Fact]
    public void RecordsOneSampleAndKeepsTheReturnValue() {
        Patch(nameof(Targets.Add), "test.concord_add");

        int result = Targets.Add(7, 35);

        result.Should().Be(42);
        _sink.Samples.Should().HaveCount(1);
    }

    // regression: a target that throws must still emit its Stop. Harmony's finally splicing is
    // proven by MethodTransplanterTests; Concord's exclusive EndExceptionBlock is not.
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

    // the auto-instrument pump wants worker threads; concord has to survive concurrent Patch
    // calls and the patched methods have to still compute the right thing.
    [Fact]
    [Trait("Category", "Benchmark")]
    public void ParallelPatchIsSafeAndReportsItsSpeedup() {
        SectionCatalog.RegisterCorePack();
        const int warm = 8;
        const int n = 48;
        MethodInfo[] methods = BuildBenchTargets(warm + n * 2);

        for (int i = 0; i < warm; i++)
            _backend.Patch(methods[i]);

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = warm; i < warm + n; i++)
            _backend.Patch(methods[i]);
        sw.Stop();
        double sequentialUs = sw.Elapsed.TotalMilliseconds * 1000.0 / n;

        sw.Restart();
        System.Threading.Tasks.Parallel.For(0, 4, lane => {
            for (int i = warm + n + lane; i < warm + n * 2; i += 4)
                _backend.Patch(methods[i]);
        });
        sw.Stop();
        double parallelUs = sw.Elapsed.TotalMilliseconds * 1000.0 / n;

        for (int i = 0; i < warm + n * 2; i++)
            methods[i].Invoke(null, null).Should().Be(i, "a parallel patch must not corrupt the body");

        Console.WriteLine(
            $"concord patch: sequential {sequentialUs:F1} us, 4-thread wall {parallelUs:F1} us, "
            + $"speedup {sequentialUs / parallelUs:F2}x");
    }

    private static MethodInfo[] BuildBenchTargets(int count) {
        System.Reflection.Emit.AssemblyBuilder asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"ConcordBench_{Guid.NewGuid():N}"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        System.Reflection.Emit.TypeBuilder type = asm.DefineDynamicModule("Main")
            .DefineType("BenchTargets", TypeAttributes.Public | TypeAttributes.Class);
        for (int i = 0; i < count; i++) {
            System.Reflection.Emit.MethodBuilder m = type.DefineMethod(
                $"Target{i}", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            System.Reflection.Emit.ILGenerator il = m.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, i);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
        }
        Type built = type.CreateType();
        MethodInfo[] methods = new MethodInfo[count];
        for (int i = 0; i < count; i++)
            methods[i] = built.GetMethod($"Target{i}")!;
        return methods;
    }

    // regression: concord's At.Finally misses yield-return exit paths, so an iterator MoveNext
    // leaked a profiler frame per yield and pinned the thread. those bodies take the transpiler.
    [Fact]
    public void IteratorMoveNextStaysBalancedAcrossYields() {
        SectionCatalog.RegisterCorePack();
        MethodInfo moveNext = IteratorMoveNextTarget();
        SectionCatalog.RegisterDirect("test.concord_iterator", moveNext);
        _backend.Patch(moveNext);
        SectionHandle after = SectionRegistry.Register("test.concord_after_iterator");

        int sum = 0;
        foreach (int value in IteratorFixture.Numbers())
            sum += value;
        sum.Should().Be(6);

        _sink.Samples.Clear();
        long token = Profiler.Start(after);
        Profiler.Stop(after, token);
        // an unbalanced iterator would leave leaked frames and misparent or swallow this.
        _sink.Samples.Should().ContainSingle(s => s.SectionId == after.Id && s.ParentId == Profiler.NoParent);
    }

    private static MethodInfo IteratorMoveNextTarget() {
        foreach (Type nested in typeof(IteratorFixture).GetNestedTypes(BindingFlags.NonPublic)) {
            MethodInfo? moveNext = nested.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
            if (moveNext != null)
                return moveNext;
        }
        throw new InvalidOperationException("iterator state machine not found");
    }

    private static class IteratorFixture {
        public static System.Collections.Generic.IEnumerable<int> Numbers() {
            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    // regression: CONC123. an async target's body compiles into a generated MoveNext, and
    // Concord refuses At.Transpiler on the declared method rather than time its setup.
    [Fact]
    public void PatchesAnAsyncTargetThroughItsStateMachine() {
        SectionCatalog.RegisterCorePack();

        Action act = () => _backend.Patch(typeof(AsyncTarget).GetMethod(nameof(AsyncTarget.Work))!);

        act.Should().NotThrow();
    }

    private static class AsyncTarget {
        public static async System.Threading.Tasks.Task Work() => await System.Threading.Tasks.Task.Yield();
    }
}
