using RimWorks.RimObs.Observers;
using FluentAssertions;
using Xunit;

namespace RimWorks.RimObs.Tests;

public sealed class AllocatedBytesReaderTests {
    [Fact]
    public void GetTotalBytes_falls_back_to_GetTotalMemory_when_no_native_counter_resolves() {
        // net10/CoreCLR has none of the Boehm libraries loaded, so this exercises the real
        // p/invoke-failure path, not a fake one.
        long bytes = AllocatedBytesReader.GetTotalBytes();

        bytes.Should().BeGreaterThan(0);
    }
}
