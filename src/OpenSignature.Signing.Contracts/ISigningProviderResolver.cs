using OpenSignature.Domain.Enums;

namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Selects a registered <see cref="ISigningProvider"/> from request or configuration.
/// </summary>
public interface ISigningProviderResolver
{
    /// <summary>
    /// Resolves a signing provider.
    /// When <paramref name="providerId"/> is provided, matches that instance (and verifies
    /// <paramref name="providerType"/> when the provider is found).
    /// When <paramref name="providerId"/> is omitted, selects the unique registered provider
    /// of <paramref name="providerType"/>.
    /// </summary>
    /// <exception cref="UnsupportedSigningProviderException">
    /// Thrown when no matching provider is registered, the id/type combination does not match,
    /// or multiple providers share the same type and no id was supplied.
    /// </exception>
    ISigningProvider Resolve(SigningProviderType providerType, string? providerId = null);

    /// <summary>Returns all registered providers (configuration inventory).</summary>
    IReadOnlyList<ISigningProvider> GetProviders();
}
