using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Infrastructure.Storage;

namespace OpenSignature.Infrastructure;

/// <summary>
/// DI helpers for infrastructure adapters.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL persistence via EF Core (<see cref="OpenSignatureDbContext"/>).
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<OpenSignatureDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }

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

    /// <summary>
    /// Registers <see cref="RabbitMqSigningJobPublisher"/> as the <see cref="ISigningJobPublisher"/> implementation.
    /// </summary>
    public static IServiceCollection AddRabbitMqPublisher(
        this IServiceCollection services,
        Action<RabbitMqOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddSingleton<ISigningJobPublisher, RabbitMqSigningJobPublisher>();
        return services;
    }
}
