using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing;

/// <summary>
/// DI helpers for signing providers.
/// </summary>
public static class SigningServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PfxSigningProvider"/> as a singleton <see cref="ISigningProvider"/>.
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
        return services;
    }
}
