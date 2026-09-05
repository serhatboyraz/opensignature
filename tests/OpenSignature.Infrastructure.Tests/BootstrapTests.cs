namespace OpenSignature.Infrastructure.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void Infrastructure_assembly_is_loadable()
    {
        Assert.Equal("OpenSignature.Infrastructure", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
