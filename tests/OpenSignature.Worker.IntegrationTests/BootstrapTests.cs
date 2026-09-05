namespace OpenSignature.Worker.IntegrationTests;

public sealed class BootstrapTests
{
    [Fact]
    public void Worker_host_entry_assembly_is_loadable()
    {
        var workerType = typeof(Worker);
        Assert.Equal("OpenSignature.Worker", workerType.Assembly.GetName().Name);
    }
}
