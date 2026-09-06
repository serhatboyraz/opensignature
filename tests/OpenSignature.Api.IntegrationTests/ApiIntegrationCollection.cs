namespace OpenSignature.Api.IntegrationTests;

/// <summary>
/// Serializes API integration tests that use process-wide environment variables
/// and Testcontainers to avoid cross-class configuration races.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiIntegrationCollection
{
    public const string Name = "ApiIntegration";
}
