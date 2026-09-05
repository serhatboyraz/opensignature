using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing;

/// <summary>
/// DI helpers for signing providers.
/// </summary>
public static class SigningServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISigningProviderResolver"/> so providers registered as
    /// <see cref="ISigningProvider"/> can be selected by type or provider id.
    /// Safe to call multiple times (registration is idempotent).
    /// </summary>
    public static IServiceCollection AddSigningProviderResolver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISigningProviderResolver>(static sp =>
            new SigningProviderSelector(sp.GetServices<ISigningProvider>()));

        return services;
    }

    /// <summary>
    /// Registers <see cref="PfxSigningProvider"/> as a singleton <see cref="ISigningProvider"/>
    /// and ensures <see cref="ISigningProviderResolver"/> is available.
    /// Password resolution uses <see cref="InMemorySigningSecretProvider"/> when secrets are supplied;
    /// otherwise <see cref="PfxSigningProviderOptions.Password"/> is used (development only).
    /// </summary>
    public static IServiceCollection AddPfxSigningProvider(
        this IServiceCollection services,
        Action<PfxSigningProviderOptions> configure,
        IEnumerable<KeyValuePair<string, string>>? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        if (secrets is not null)
        {
            services.AddSingleton<ISigningSecretProvider>(new InMemorySigningSecretProvider(secrets));
        }

        services.AddSingleton<ISigningProvider, PfxSigningProvider>();
        services.AddSigningProviderResolver();
        return services;
    }
}
