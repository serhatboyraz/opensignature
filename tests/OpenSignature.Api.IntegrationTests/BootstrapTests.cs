namespace OpenSignature.Api.IntegrationTests;

public sealed class BootstrapTests
{
    [Fact]
    public void Api_program_type_is_available_for_web_application_factory()
    {
        var programType = typeof(Program);
        Assert.Equal("OpenSignature.Api", programType.Assembly.GetName().Name);
    }
}
