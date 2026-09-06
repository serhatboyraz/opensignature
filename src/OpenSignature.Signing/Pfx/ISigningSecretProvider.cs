namespace OpenSignature.Signing.Pfx;

/// <summary>
/// Resolves named secrets for signing providers (e.g. PFX passwords).
/// Prefer wrapping <c>ISecretStore</c> via <see cref="SecretStoreSigningSecretProvider"/>.
/// Implementations must never log secret values.
/// </summary>
public interface ISigningSecretProvider
{
    /// <summary>
    /// Returns the secret value for <paramref name="secretName"/>, or <c>null</c> when not found.
    /// </summary>
    string? GetSecret(string secretName);
}
