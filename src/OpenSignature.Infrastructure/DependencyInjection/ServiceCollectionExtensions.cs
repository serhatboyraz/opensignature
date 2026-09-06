using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Abstractions.Persistence;
using OpenSignature.Application.Abstractions.Signatures;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Application.Abstractions.Verification;
using OpenSignature.Application.Signatures;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Infrastructure.Signatures;
using OpenSignature.Infrastructure.Storage;
using OpenSignature.Infrastructure.Verification;
using OpenSignature.Validation;

namespace OpenSignature.Infrastructure;

/// <summary>
/// DI helpers for infrastructure adapters.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL persistence via EF Core (<see cref="OpenSignatureDbContext"/>)
    /// and the signature-request idempotency store.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<OpenSignatureDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ISignatureRequestIdempotencyStore, EfSignatureRequestIdempotencyStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISigningJobLockService, EfSigningJobLockService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISignatureRequestService"/> (requires persistence, storage, and outbox writer).
    /// </summary>
    public static IServiceCollection AddSignatureRequestService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISignatureRequestService, EfSignatureRequestService>();
        return services;
    }

    /// <summary>
    /// Registers signature verification (Phase 9 validators + stored/uploaded report use case).
    /// </summary>
    public static IServiceCollection AddSignatureVerification(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenSignatureValidation();
        services.AddScoped<ISignatureVerificationService, SignatureVerificationService>();
        return services;
    }

    /// <summary>
    /// Binds <see cref="SignatureApiOptions"/> from configuration.
    /// </summary>
    public static IServiceCollection AddSignatureApiOptions(
        this IServiceCollection services,
        Action<SignatureApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<SignatureApiOptions>(_ => { });
        }

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

    /// <summary>
    /// Registers transactional outbox writer, processor, and background publisher.
    /// Requires <see cref="AddPersistence"/> and an <see cref="ISigningJobPublisher"/> registration.
    /// </summary>
    public static IServiceCollection AddOutboxPublisher(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<OutboxOptions>(_ => { });
        }

        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddHostedService<OutboxPublisherHostedService>();
        return services;
    }
}
