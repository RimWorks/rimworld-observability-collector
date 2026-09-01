using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using HarmonyLib;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class MethodTransplanterTests : IDisposable {
    private readonly RecordingSink _sink;
    private readonly Harmony _harmony;
    private readonly List<MethodBase> _patched = new();

    public MethodTransplanterTests() {
        _sink = new RecordingSink();
        Profiler.SetSink(_sink);
        Profiler.Enabled = true;
        _harmony = new Harmony($"RimWorks.RimObs.tests.{Guid.NewGuid():N}");
    }

    public void Dispose() {
        foreach (MethodBase m in _patched) {
            try {
                _harmony.Unpatch(m, HarmonyPatchType.All, _harmony.Id);
            }
            catch {
            }
        }
        Profiler.SetSink(null);
    }

    private void Patch(MethodInfo target, string sectionName) {
        SectionCatalog.RegisterDirect(sectionName, target);
        HarmonyMethod transpiler = new(MethodTransplanter.TranspilerMethod);
        _harmony.Patch(target, transpiler: transpiler);
        _patched.Add(target);
    }

    [Fact]
    public void Patches_void_method_and_records_one_sample() {
        Patch(typeof(Targets).GetMethod(nameof(Targets.VoidNoOp))!, "test.void_noop");

        Targets.VoidNoOp();

        _sink.Samples.Should().HaveCount(1);
        _sink.Samples[0].SectionId.Should().BeGreaterOrEqualTo(0);
        _sink.Samples[0].ElapsedTicks.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void Patches_returning_method_and_preserves_return_value() {
        Patch(typeof(Targets).GetMethod(nameof(Targets.Add))!, "test.add");

        int result = Targets.Add(7, 35);

        result.Should().Be(42);
        _sink.Samples.Should().HaveCount(1);
    }

    [Fact]
    public void Records_sample_even_when_method_throws() {
        Patch(typeof(Targets).GetMethod(nameof(Targets.ThrowsAlways))!, "test.throws");

        Action act = () => Targets.ThrowsAlways();

        act.Should().Throw<InvalidOperationException>().WithMessage("nope");
        _sink.Samples.Should().HaveCount(1);
    }

    [Fact]
    public void Inactive_section_does_not_record_sample() {
        MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.ReturnsConst))!;
        CatalogEntry entry = SectionCatalog.RegisterDirect("test.const_inactive", target);
        SectionRegistry.SetActive(entry.SectionId, false);

        HarmonyMethod transpiler = new(MethodTransplanter.TranspilerMethod);
        _harmony.Patch(target, transpiler: transpiler);
        _patched.Add(target);

        int result = Targets.ReturnsConst();

        result.Should().Be(99);
        _sink.Samples.Should().BeEmpty();
    }

    [Fact]
    public void Empty_body_emits_paired_start_and_stop_with_ret() {
        MethodInfo target = typeof(Targets).GetMethod(nameof(Targets.VoidNoOp))!;
        SectionCatalog.RegisterDirect("test.empty_body", target);

        System.Reflection.Emit.DynamicMethod dyn = new(
            "empty_body_test",
            typeof(void),
            Type.EmptyTypes,
            typeof(MethodTransplanterTests).Module,
            skipVisibility: true
        );
        System.Reflection.Emit.ILGenerator gen = dyn.GetILGenerator();

        List<CodeInstruction> emitted = new(MethodTransplanter.Transpile(Array.Empty<CodeInstruction>(), gen, target));

        MethodInfo startById = typeof(Profiler).GetMethod(
            nameof(Profiler.StartById),
            BindingFlags.Public | BindingFlags.Static
        )!;
        MethodInfo stopById = typeof(Profiler).GetMethod(
            nameof(Profiler.StopById),
            BindingFlags.Public | BindingFlags.Static
        )!;

        int startCalls = 0;
        int stopCalls = 0;
        foreach (CodeInstruction inst in emitted) {
            if (inst.opcode == System.Reflection.Emit.OpCodes.Call && inst.operand is MethodInfo m) {
                if (ReferenceEquals(m, startById))
                    startCalls++;
                else if (ReferenceEquals(m, stopById))
                    stopCalls++;
            }
        }
        startCalls.Should().Be(1, "an empty body must still pair its emitted Start with a Stop");
        stopCalls.Should().Be(1, "an empty body must still emit the matching Stop");
        emitted[^1].opcode.Should().Be(System.Reflection.Emit.OpCodes.Ret);
    }

    [Fact]
    public void Branchy_method_with_multiple_returns_still_records_once() {
        Patch(typeof(Targets).GetMethod(nameof(Targets.MultipleReturns))!, "test.multi_returns");

        Targets.MultipleReturns(0).Should().Be("zero");
        Targets.MultipleReturns(1).Should().Be("one");
        Targets.MultipleReturns(2).Should().Be("two");
        Targets.MultipleReturns(99).Should().Be("other");

        _sink.Samples.Should().HaveCount(4);
    }

    public static class Targets {
        public static int Counter;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void VoidNoOp() {
            Counter++;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Add(int a, int b) {
            return a + b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ReturnsConst() {
            return 99;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowsAlways() {
            throw new InvalidOperationException("nope");
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
    }
}
