using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorks.RimObs.Library.Control;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests.Control;

public class AssemblyIndexTests {
    [Fact]
    public void Enumerate_returns_user_assemblies_and_skips_bcl() {
        IEnumerable<Assembly> assemblies = AssemblyIndex.Enumerate();
        IEnumerable<string> names = assemblies.Select(a => a.GetName().Name ?? string.Empty);

        // FluentAssertions is a direct non-BCL dependency and must appear.
        names.Should().Contain("FluentAssertions");
        // BCL/runtime never appears.
        names.Should().NotContain("System.Private.CoreLib");
        names.Should().NotContain("mscorlib");
        names.Should().NotContain("System.Runtime");
        names.Should().NotContain("netstandard");
        // RimObs.* assemblies are excluded (implementation intentionally skips them).
        names.Should().NotContain("RimObs.Library.Tests");
    }
}
