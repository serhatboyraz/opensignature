using OpenSignature.Signing;

namespace OpenSignature.Interop.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void Interop_suite_references_signing_assembly()
    {
        Assert.Equal("OpenSignature.Signing", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
