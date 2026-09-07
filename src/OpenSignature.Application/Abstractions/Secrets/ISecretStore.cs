namespace OpenSignature.Application.Abstractions.Secrets;

/// <summary>
/// Resolves named application secrets. Implementations must never log secret values.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Returns the secret for <paramref name="secretName"/>, or <c>null</c> when not found.
    /// </summary>
    ValueTask<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}
