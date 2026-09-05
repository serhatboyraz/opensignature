using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing;

/// <summary>
/// Resolves a signing provider from the set registered in DI / configuration.
/// </summary>
public sealed class SigningProviderSelector : ISigningProviderResolver
{
    private readonly IReadOnlyList<ISigningProvider> _providers;

    public SigningProviderSelector(IEnumerable<ISigningProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
    }

    public IReadOnlyList<ISigningProvider> GetProviders() => _providers;

    public ISigningProvider Resolve(SigningProviderType providerType, string? providerId = null)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            return ResolveById(providerType, providerId.Trim());
        }

        return ResolveByType(providerType);
    }

    private ISigningProvider ResolveById(SigningProviderType providerType, string providerId)
    {
        var matches = _providers
            .Where(provider => string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            throw new UnsupportedSigningProviderException(
                $"Signing provider '{providerId}' is not registered.",
                providerType,
                providerId);
        }

        if (matches.Length > 1)
        {
            throw new UnsupportedSigningProviderException(
                $"Multiple signing providers are registered with id '{providerId}'. Provider ids must be unique.",
                providerType,
                providerId);
        }

        var provider = matches[0];
        if (provider.ProviderType != providerType)
        {
            throw new UnsupportedSigningProviderException(
                $"Signing provider '{providerId}' is registered as {provider.ProviderType}, but {providerType} was requested.",
                providerType,
                providerId);
        }

        return provider;
    }

    private ISigningProvider ResolveByType(SigningProviderType providerType)
    {
        var matches = _providers
            .Where(provider => provider.ProviderType == providerType)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new UnsupportedSigningProviderException(
                $"No signing provider is registered for type '{providerType}'.",
                providerType);
        }

        if (matches.Length > 1)
        {
            var ids = string.Join(", ", matches.Select(provider => provider.ProviderId));
            throw new UnsupportedSigningProviderException(
                $"Multiple signing providers are registered for type '{providerType}' ({ids}). Specify a provider id.",
                providerType);
        }

        return matches[0];
    }
}
