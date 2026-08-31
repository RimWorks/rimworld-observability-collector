using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace RimWorks.RimObs.Library.Tests.Benchmarks;

public sealed class ObservedSectionScanBench : IDisposable {
    private readonly ITestOutputHelper _output;

    public ObservedSectionScanBench(ITestOutputHelper output) {
        _output = output;
        PatchInstaller.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        OwnerRegistry.Clear();
    }

    public void Dispose() {
        PatchInstaller.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        OwnerRegistry.Clear();
    }

    [Fact]
    public void Scan_HundredAttributedMethods_CompletesAndIsWellFormed() {
        Assembly synthetic = BuildSyntheticAssembly(
            attributedTypeCount: 10,
            methodsPerType: 10,
            unattributedTypeCount: 10);

        OwnerRegistry.RegisterMod(synthetic, "bench.modid");
        IReadOnlyList<Assembly> asms = [synthetic];

        Stopwatch sw = Stopwatch.StartNew();
        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            [("bench.modid", asms)]);
        sw.Stop();

        _output.WriteLine(
            $"Scan duration: {sw.Elapsed.TotalMilliseconds:F2}ms for {result.AttributesFound} attributes");

        result.AssembliesScanned.Should().Be(1);
        result.AttributesFound.Should().Be(100);
        result.Registered.Should().Be(100);
        result.Failed.Should().Be(0);
        result.SkippedDuplicate.Should().Be(0);
        result.SkippedUnsupported.Should().Be(0);
    }

    private static AssemblyBuilder BuildSyntheticAssembly(
        int attributedTypeCount,
        int methodsPerType,
        int unattributedTypeCount) {
        AssemblyName name = new($"RimObsBench_{Guid.NewGuid():N}");
        AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(
            name, AssemblyBuilderAccess.Run);
        ModuleBuilder mod = asm.DefineDynamicModule("Main");

        ConstructorInfo attrCtor =
            typeof(ObservedSectionAttribute).GetConstructor(Type.EmptyTypes)!;
        CustomAttributeBuilder attrBuilder =
            new(attrCtor, Array.Empty<object>());

        for (int t = 0; t < attributedTypeCount; t++) {
            TypeBuilder type = mod.DefineType(
                $"BenchType{t}", TypeAttributes.Public | TypeAttributes.Class);
            for (int m = 0; m < methodsPerType; m++) {
                MethodBuilder method = type.DefineMethod(
                    $"M{m}",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    Type.EmptyTypes);
                method.SetCustomAttribute(attrBuilder);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ret);
            }
            type.CreateType();
        }

        for (int t = 0; t < unattributedTypeCount; t++) {
            TypeBuilder type = mod.DefineType(
                $"InertType{t}", TypeAttributes.Public | TypeAttributes.Class);
            MethodBuilder method = type.DefineMethod(
                "Noop",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ret);
            type.CreateType();
        }

        return asm;
    }
}
