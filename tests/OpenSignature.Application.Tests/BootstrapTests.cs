namespace OpenSignature.Application.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void Application_assembly_is_loadable()
    {
        Assert.Equal("OpenSignature.Application", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
