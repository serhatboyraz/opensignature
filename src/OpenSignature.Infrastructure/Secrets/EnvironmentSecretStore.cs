using OpenSignature.Application.Abstractions.Secrets;

namespace OpenSignature.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from environment variables named <c>OPENSIGNATURE_SECRET_{NAME}</c>.
/// ':' / '.' / '-' in the secret name become '_'. Never logs secret values.
/// </summary>
public sealed class EnvironmentSecretStore : ISecretStore
{
    public const string VariablePrefix = "OPENSIGNATURE_SECRET_";

    private readonly Func<string, string?> _readVariable;

    public EnvironmentSecretStore()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    public EnvironmentSecretStore(Func<string, string?> readVariable)
    {
        _readVariable = readVariable ?? throw new ArgumentNullException(nameof(readVariable));
    }

    public static string ToEnvironmentVariableName(string secretName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        var normalized = secretName.Trim()
            .Replace(':', '_')
            .Replace('.', '_')
            .Replace('-', '_')
            .ToUpperInvariant();

        return VariablePrefix + normalized;
    }

    /// <inheritdoc />
    public ValueTask<string?> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envName = ToEnvironmentVariableName(secretName);
        return ValueTask.FromResult(_readVariable(envName));
    }
}
