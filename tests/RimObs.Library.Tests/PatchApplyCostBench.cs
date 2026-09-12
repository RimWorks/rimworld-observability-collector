using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using RimWorks.RimObs.Library.Control;
using RimWorks.RimObs.Patches.Harmony;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RimWorks.RimObs.Tests;

// where do the ~1.4 ms per auto-instrument patch go? times each layer on real methods with
// the real Harmony backend. informational; the assert only catches a gross regression.
public sealed class PatchApplyCostBench : IDisposable {
    private readonly ITestOutputHelper _out;
    private readonly HarmonyBackend _backend = new();

    public PatchApplyCostBench(ITestOutputHelper output) {
        _out = output;
        PatchBackends.ResetForTests();
        PatchRegistry.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
    }

    public void Dispose() {
        _backend.UnpatchAllForTests();
        PatchBackends.ResetForTests();
        PatchRegistry.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Decompose_one_patch_apply() {
        const int warm = 16;
        const int n = 96;
        MethodInfo[] methods = BuildTargets(warm + n * 3);

        // harmony JITs its own machinery on the first patches; burn that before timing.
        for (int i = 0; i < warm; i++)
            _backend.Patch(methods[i]);

        double signature = MeasurePer(methods, warm, n, m => MethodResolver.BuildSignature(m));

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = warm; i < warm + n; i++)
            _backend.Patch(methods[i]);
        sw.Stop();
        double backendPatch = sw.Elapsed.TotalMilliseconds / n;

        PatchBackends.Register(_backend, PatchBackends.HarmonyPriority);
        PatchBackends.SelectBest(scan: false);
        sw.Restart();
        for (int i = warm + n; i < warm + n * 2; i++)
            PatchRegistry.Apply("bench.owner", methods[i], MethodResolver.BuildSignature(methods[i]));
        sw.Stop();
        double fullApply = sw.Elapsed.TotalMilliseconds / n;

        sw.Restart();
        System.Threading.Tasks.Parallel.For(0, 4, lane => {
            for (int i = warm + n * 2 + lane; i < warm + n * 3; i += 4)
                _backend.Patch(methods[i]);
        });
        sw.Stop();
        double parallelPer = sw.Elapsed.TotalMilliseconds / n;

        _out.WriteLine($"BuildSignature      {signature * 1000.0,9:F1} us");
        _out.WriteLine($"backend.Patch warm  {backendPatch * 1000.0,9:F1} us");
        _out.WriteLine($"registry Apply      {fullApply * 1000.0,9:F1} us  (bookkeeping = {(fullApply - backendPatch) * 1000.0:F1})");
        _out.WriteLine($"4-thread wall/patch {parallelPer * 1000.0,9:F1} us  (speedup = {backendPatch / parallelPer:F2}x)");

        fullApply.Should().BeLessThan(50.0);
    }

    private static double MeasurePer(MethodInfo[] methods, int from, int count, Action<MethodInfo> op) {
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = from; i < from + count; i++)
            op(methods[i]);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / count;
    }

    private static MethodInfo[] BuildTargets(int count) {
        AssemblyName name = new($"RimObsPatchBench_{Guid.NewGuid():N}");
        AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        ModuleBuilder mod = asm.DefineDynamicModule("Main");
        TypeBuilder type = mod.DefineType("BenchTargets", TypeAttributes.Public | TypeAttributes.Class);
        for (int i = 0; i < count; i++) {
            MethodBuilder m = type.DefineMethod(
                $"Target{i}", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            ILGenerator il = m.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ret);
        }
        Type built = type.CreateType();
        MethodInfo[] methods = new MethodInfo[count];
        for (int i = 0; i < count; i++)
            methods[i] = built.GetMethod($"Target{i}")!;
        return methods;
    }
}
