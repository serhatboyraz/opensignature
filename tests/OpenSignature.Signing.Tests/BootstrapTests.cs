namespace OpenSignature.Signing.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void Signing_assembly_is_loadable()
    {
        Assert.Equal("OpenSignature.Signing", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
