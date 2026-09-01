using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorks.RimObs.Api;
using RimWorks.RimObs.Patching;
using RimWorks.RimObs.Profile;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public class ObservedSectionScannerTests : System.IDisposable {
    public ObservedSectionScannerTests() {
        TestBackend.Activate();
        PatchInstaller.ResetForTests();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        OwnerRegistry.Clear();
        OwnerRegistry.RegisterMod(typeof(ObservedSectionScannerTests).Assembly, "test.modid");
    }

    public void Dispose() {
        PatchInstaller.ResetForTests();
        TestBackend.Deactivate();
        SectionCatalog.Clear();
        SectionRegistry.Clear();
        OwnerRegistry.Clear();
        System.GC.SuppressFinalize(this);
    }

    public class Target_BareAttribute {
        [ObservedSection]
        public void Tick() { }
    }

    public class Target_NamedAttribute {
        [ObservedSection("custom_name")]
        public void Run() { }
    }

    public class Target_Subsystem {
        [ObservedSection("with_sub", Subsystem = "jobs")]
        public void Run() { }
    }

    [Fact]
    public void Scan_BareAttribute_RegistersWithFullTypeAndMethodName() {
        IReadOnlyList<Assembly> asms = [typeof(Target_BareAttribute).Assembly];
        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            [("test.modid", asms)]);

        result.AttributesFound.Should().BeGreaterOrEqualTo(1);
        result.Registered.Should().BeGreaterOrEqualTo(1);

        string expected = $"test.modid.{typeof(Target_BareAttribute).FullName}.Tick";
        SectionCatalog.Entries.Should().Contain(e => e.Name == expected);
    }

    [Fact]
    public void Scan_NamedAttribute_UsesProvidedName() {
        IReadOnlyList<Assembly> asms = [typeof(Target_NamedAttribute).Assembly];
        ObservedSectionScanner.Scan([("test.modid", asms)]);

        SectionCatalog.Entries.Should().Contain(e => e.Name == "test.modid.custom_name");
    }

    [Fact]
    public void Scan_SubsystemAttribute_PropagatesToEntryAndRegistry() {
        IReadOnlyList<Assembly> asms = [typeof(Target_Subsystem).Assembly];
        ObservedSectionScanner.Scan([("test.modid", asms)]);

        CatalogEntry entry = SectionCatalog.Entries.First(e => e.Name == "test.modid.with_sub");
        entry.Subsystem.Should().Be("jobs");
        SectionRegistry.GetSubsystem(entry.SectionId).Should().Be("jobs");
    }

    public abstract class Target_Abstract {
        [ObservedSection]
        public abstract void Tick();
    }

    public class Target_GenericOpen<T> {
        [ObservedSection]
        public void Tick(T item) { }
    }

    public class Target_Async {
        [ObservedSection]
        public async System.Threading.Tasks.Task DoAsync() { await System.Threading.Tasks.Task.Yield(); }
    }

    public class Target_Iterator {
        [ObservedSection]
        public System.Collections.Generic.IEnumerable<int> Yield() { yield return 1; }
    }

    [Theory]
    [InlineData(typeof(Target_Abstract), nameof(Target_Abstract.Tick))]
    [InlineData(typeof(Target_GenericOpen<>), "Tick")]
    [InlineData(typeof(Target_Async), nameof(Target_Async.DoAsync))]
    [InlineData(typeof(Target_Iterator), nameof(Target_Iterator.Yield))]
    public void Scan_UnsupportedMethod_Skipped(System.Type type, string methodName) {
        IReadOnlyList<Assembly> asms = new[] { type.Assembly };
        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            new[] { ("test.modid", asms) });

        result.SkippedUnsupported.Should().BeGreaterOrEqualTo(1);
        string typeName = type.FullName!;
        SectionCatalog.Entries.Should().NotContain(
            e => e.TypeName == typeName && e.MethodName == methodName);
    }

    public class Target_Dedup {
        [ObservedSection("dup_name")]
        public void TargetMethod() { }
    }

    [Fact]
    public void Scan_DuplicateAgainstXmlSeeded_Skipped() {
        MethodInfo target = typeof(Target_Dedup).GetMethod(nameof(Target_Dedup.TargetMethod))!;
        SectionCatalog.Register(
            "test.modid.xml_owned",
            target.DeclaringType!.FullName!,
            target.Name,
            System.Array.Empty<string>(),
            subsystem: null);
        SectionCatalog.ResolveAll();

        IReadOnlyList<Assembly> asms = new[] { typeof(Target_Dedup).Assembly };
        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            new[] { ("test.modid", asms) });

        result.SkippedDuplicate.Should().BeGreaterOrEqualTo(1);
        SectionCatalog.Entries
            .Where(e => ReferenceEquals(e.Resolved, target))
            .Should().HaveCount(1, "scanner must not register a second entry for the XML-seeded MethodBase");
    }

    private sealed class PartialLoadAssembly : Assembly {
        private readonly Type?[] _types;
        private readonly string _name;

        public PartialLoadAssembly(string name, Type?[] types) {
            _name = name;
            _types = types;
        }

        public override AssemblyName GetName() => new AssemblyName(_name);

        public override Type[] GetTypes() {
            Exception[] loaderExceptions = new Exception[] { new TypeLoadException("simulated partial load") };
            throw new ReflectionTypeLoadException(_types, loaderExceptions, "partial load");
        }
    }

    [Fact]
    public void Scan_ReflectionTypeLoadException_Continues() {
        Type?[] partial = new Type?[] { typeof(Target_BareAttribute), null };
        PartialLoadAssembly badAssembly = new PartialLoadAssembly("test.partial", partial);

        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            new[] { ("test.modid", (IReadOnlyList<Assembly>)new Assembly[] { badAssembly }) });

        result.AssembliesScanned.Should().Be(1);
        result.Warnings.Should().Contain(w =>
            w.Contains("simulated partial load", System.StringComparison.OrdinalIgnoreCase) ||
            w.Contains("partial load", System.StringComparison.OrdinalIgnoreCase));
        result.AttributesFound.Should().BeGreaterOrEqualTo(1);
    }

    public static class Target_PInvoke {
        [ObservedSection("pinvoke_method")]
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        internal static extern bool Beep(uint dwFreq, uint dwDuration);
    }

    [Fact]
    public void Scan_PatchInstallFailure_Recorded() {
        MethodInfo rejectedMethod = typeof(Target_PInvoke).GetMethod(nameof(Target_PInvoke.Beep), BindingFlags.NonPublic | BindingFlags.Static)!;
        SectionCatalog.RegisterDirect("test.modid.pinvoke_method", rejectedMethod);

        int beforeFailed = PatchInstaller.FailedCount;
        PatchInstaller.PatchAttributeMethod(rejectedMethod);

        PatchInstaller.FailedCount.Should().BeGreaterOrEqualTo(beforeFailed + 1);
        CatalogEntry entry = SectionCatalog.Entries.First(e => e.Name == "test.modid.pinvoke_method");
        entry.Installed.Should().BeFalse();
        entry.InstallError.Should().BeOfType<NotSupportedException>(
            "P/Invoke methods have no patchable IL body and must be rejected deterministically, "
            + "not left to Harmony's environment-dependent behavior");
    }

    [Fact]
    public void Scan_DuplicateAgainstExistingCatalog_Skipped() {
        MethodBase target = typeof(Target_Dedup).GetMethod(nameof(Target_Dedup.TargetMethod))!;
        SectionCatalog.RegisterDirect("test.modid.core_owned", target);

        IReadOnlyList<Assembly> asms = new[] { typeof(Target_Dedup).Assembly };
        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            new[] { ("test.modid", asms) });

        result.SkippedDuplicate.Should().BeGreaterOrEqualTo(1);
        SectionCatalog.Entries
            .Where(e => ReferenceEquals(e.Resolved, target))
            .Should().HaveCount(1, "scanner must not register a second entry for the same MethodBase");
    }

    public static class Target_PatchedAtScan {
        [ObservedSection("patched_by_scan")]
        public static int Echo(int x) => x;
    }

    [Fact]
    public void Scan_RegistersAndPatches() {
        int beforeInstalled = PatchInstaller.InstalledCount;
        IReadOnlyList<Assembly> asms = new[] { typeof(Target_PatchedAtScan).Assembly };
        ObservedSectionScanner.Scan(new[] { ("test.modid", asms) });

        PatchInstaller.InstalledCount.Should().BeGreaterOrEqualTo(beforeInstalled + 1);
        CatalogEntry entry = SectionCatalog.Entries.First(e => e.Name == "test.modid.patched_by_scan");
        entry.Installed.Should().BeTrue();
    }

    // The library ships as assembly name "RimObs" (see RimObs.Library.csproj <AssemblyName>).
    // In this test project the library is source-linked into "RimObs.Library.Tests",
    // so typeof(Profiler).Assembly.GetName().Name is "RimObs.Library.Tests" here.
    // We verify the guard does NOT exclude the test assembly (wrong name), proving
    // the guard is name-exact and only fires for "RimObs".
    // The library ships as assembly name "RimObs" (see RimObs.Library.csproj <AssemblyName>).
    // In this test project the library is source-linked, so we fabricate a dynamic assembly
    // named "RimObs" to verify the guard skips it.
    [Fact]
    public void Scan_SkipsRimObsLibraryItself() {
        System.Reflection.Emit.AssemblyBuilder rimobs = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("RimObs"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        rimobs.GetName().Name.Should().Be("RimObs");

        IReadOnlyList<Assembly> asms = new[] { (Assembly)rimobs };
        ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
            new[] { ("rimworks.rimobs", asms) });

        result.AssembliesScanned.Should().Be(0);
    }

    [Fact]
    public void Scan_PerMethodException_DoesNotAbortScan() {
        System.Action act = () => ObservedSectionScanner.Scan(new[] {
            ("test.modid", (IReadOnlyList<Assembly>)new[] { typeof(Target_BareAttribute).Assembly }),
            ("system", (IReadOnlyList<Assembly>)new[] { typeof(string).Assembly }),
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Scan_AttributesDisabled_NoOp() {
        ObservedSectionScanner.AttributesEnabledForTests = false;
        try {
            IReadOnlyList<Assembly> asms = new[] { typeof(Target_BareAttribute).Assembly };
            ObservedSectionScanner.ScanResult result = ObservedSectionScanner.Scan(
                new[] { ("test.modid", asms) });

            result.AssembliesScanned.Should().Be(0);
            result.AttributesFound.Should().Be(0);
            result.Registered.Should().Be(0);
        }
        finally {
            ObservedSectionScanner.AttributesEnabledForTests = null;
        }
    }
}
