namespace OpenSignature.Signing.Pfx;

/// <summary>
/// Resolves named secrets for signing providers (e.g. PFX passwords).
/// This is a stub abstraction; production secret stores arrive in T113.
/// Implementations must never log secret values.
/// </summary>
public interface ISigningSecretProvider
{
    /// <summary>
    /// Returns the secret value for <paramref name="secretName"/>, or <c>null</c> when not found.
    /// </summary>
    string? GetSecret(string secretName);
}
