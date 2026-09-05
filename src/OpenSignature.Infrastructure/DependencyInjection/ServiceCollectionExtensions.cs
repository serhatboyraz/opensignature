using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Infrastructure.Storage;

namespace OpenSignature.Infrastructure;

/// <summary>
/// DI helpers for infrastructure adapters.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LocalFileStorage"/> as the <see cref="IFileStorage"/> implementation.
    /// </summary>
    public static IServiceCollection AddLocalFileStorage(
        this IServiceCollection services,
        Action<LocalFileStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }
}
