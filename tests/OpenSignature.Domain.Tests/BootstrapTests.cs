namespace OpenSignature.Domain.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void Domain_assembly_is_loadable()
    {
        var markerType = typeof(AssemblyMarker);
        Assert.Equal("OpenSignature.Domain", markerType.Namespace);
        Assert.Equal("OpenSignature.Domain", markerType.Assembly.GetName().Name);
    }
}
