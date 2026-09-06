using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Application.Abstractions.Secrets;
using OpenSignature.Application.Security;

namespace OpenSignature.Infrastructure.Secrets;

/// <summary>
/// DI registration for secret stores.
/// </summary>
public static class SecretStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISecretStore"/>: configuration values, then environment variables,
    /// wrapped by <see cref="RotatingSecretStore"/> for a future rotation hook.
    /// Never logs secret values.
    /// </summary>
    public static IServiceCollection AddSecretStores(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SecretStoreOptions>(configuration.GetSection(SecretStoreOptions.SectionName));
        services.TryAddSingleton<ConfigurationSecretStore>();
        services.TryAddSingleton<EnvironmentSecretStore>();
        services.TryAddSingleton<ISecretStore>(static sp =>
        {
            var configurationStore = sp.GetRequiredService<ConfigurationSecretStore>();
            var environmentStore = sp.GetRequiredService<EnvironmentSecretStore>();
            var chain = new ChainedSecretStore(configurationStore, environmentStore);
            return new RotatingSecretStore(chain);
        });

        return services;
    }
}
