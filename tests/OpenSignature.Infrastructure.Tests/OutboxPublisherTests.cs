using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpenSignature.Infrastructure.Tests;

public sealed class OutboxPublisherTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("opensignature")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ProcessPending_publishes_message_and_marks_processed()
    {
        var publisher = new FakeSigningJobPublisher();
        await using var provider = BuildProvider(publisher);

        var jobMessage = CreateSampleJobMessage();
        Guid outboxId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            await db.Database.MigrateAsync();

            var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
            writer.EnqueueSigningJob(jobMessage);
            await db.SaveChangesAsync();

            outboxId = await db.OutboxMessages.Select(m => m.Id).SingleAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            var attempted = await processor.ProcessPendingAsync();
            Assert.Equal(1, attempted);
        }

        Assert.Single(publisher.Published);
        Assert.Equal(jobMessage, publisher.Published[0]);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var loaded = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            Assert.Equal(OutboxMessageStatus.Processed, loaded.Status);
            Assert.NotNull(loaded.PublishedAt);
            Assert.Equal(0, loaded.RetryCount);
            Assert.Null(loaded.Error);
        }
    }

    [Fact]
    public async Task ProcessPending_retries_after_transient_publish_failure()
    {
        var publisher = new FakeSigningJobPublisher
        {
            FailuresBeforeSuccess = 1
        };
        await using var provider = BuildProvider(publisher, options =>
        {
            options.MaxRetries = 5;
            options.BatchSize = 10;
        });

        var jobMessage = CreateSampleJobMessage();
        Guid outboxId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            await db.Database.MigrateAsync();

            var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
            writer.EnqueueSigningJob(jobMessage);
            await db.SaveChangesAsync();
            outboxId = await db.OutboxMessages.Select(m => m.Id).SingleAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            await processor.ProcessPendingAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var failed = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            Assert.Equal(OutboxMessageStatus.Failed, failed.Status);
            Assert.Equal(1, failed.RetryCount);
            Assert.NotNull(failed.Error);
        }

        Assert.Empty(publisher.Published);

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            await processor.ProcessPendingAsync();
        }

        Assert.Single(publisher.Published);
        Assert.Equal(jobMessage, publisher.Published[0]);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var loaded = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            Assert.Equal(OutboxMessageStatus.Processed, loaded.Status);
            Assert.Equal(1, loaded.RetryCount);
            Assert.Null(loaded.Error);
        }
    }

    [Fact]
    public async Task Pending_messages_are_recovered_by_new_processor_instance()
    {
        var publisher = new FakeSigningJobPublisher();
        var connectionString = _postgres.GetConnectionString();
        var jobMessage = CreateSampleJobMessage();
        Guid outboxId;

        // First "process" lifetime: enqueue only (simulates crash before publish).
        await using (var provider = BuildProvider(publisher, connectionString: connectionString, registerHostedService: false))
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            await db.Database.MigrateAsync();

            var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
            writer.EnqueueSigningJob(jobMessage);
            await db.SaveChangesAsync();
            outboxId = await db.OutboxMessages.Select(m => m.Id).SingleAsync();
        }

        Assert.Empty(publisher.Published);

        // Second lifetime after "restart": new DI root and processor recovers pending rows.
        await using (var provider = BuildProvider(publisher, connectionString: connectionString, registerHostedService: false))
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var pending = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            Assert.Equal(OutboxMessageStatus.Pending, pending.Status);

            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            var attempted = await processor.ProcessPendingAsync();
            Assert.Equal(1, attempted);
        }

        Assert.Single(publisher.Published);
        Assert.Equal(jobMessage, publisher.Published[0]);

        await using (var provider = BuildProvider(publisher, connectionString: connectionString, registerHostedService: false))
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var loaded = await db.OutboxMessages.SingleAsync(m => m.Id == outboxId);
            Assert.Equal(OutboxMessageStatus.Processed, loaded.Status);
        }
    }

    [Fact]
    public async Task Writer_enqueues_in_same_dbcontext_transaction_as_domain_row()
    {
        var publisher = new FakeSigningJobPublisher();
        await using var provider = BuildProvider(publisher);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        await db.Database.MigrateAsync();

        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var jobMessage = CreateSampleJobMessage();

        await using var transaction = await db.Database.BeginTransactionAsync();

        db.OutboxMessages.Add(OutboxMessage.Create("marker", """{"ok":true}"""));
        writer.EnqueueSigningJob(jobMessage);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Equal(2, await db.OutboxMessages.CountAsync());
        Assert.Contains(
            await db.OutboxMessages.ToListAsync(),
            m => m.Type == SigningQueueTopology.SignatureCreatedMessageType
                 && m.Status == OutboxMessageStatus.Pending);
    }

    [Fact]
    public void AddOutboxPublisher_registers_writer_processor_and_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(_postgres.GetConnectionString());
        services.AddSingleton<ISigningJobPublisher, FakeSigningJobPublisher>();
        services.AddOutboxPublisher(options =>
        {
            options.BatchSize = 25;
            options.MaxRetries = 3;
            options.PollInterval = TimeSpan.FromMilliseconds(100);
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);

        using (var scope = provider.CreateScope())
        {
            Assert.IsType<OutboxWriter>(scope.ServiceProvider.GetRequiredService<IOutboxWriter>());
            Assert.IsType<OutboxProcessor>(scope.ServiceProvider.GetRequiredService<IOutboxProcessor>());
        }

        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<OutboxPublisherHostedService>()
            .SingleOrDefault();
        Assert.NotNull(hosted);

        var options = provider.GetRequiredService<IOptions<OutboxOptions>>().Value;
        Assert.Equal(25, options.BatchSize);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.PollInterval);
    }

    private ServiceProvider BuildProvider(
        FakeSigningJobPublisher publisher,
        Action<OutboxOptions>? configure = null,
        string? connectionString = null,
        bool registerHostedService = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(connectionString ?? _postgres.GetConnectionString());
        services.AddSingleton<ISigningJobPublisher>(publisher);

        if (registerHostedService)
        {
            services.AddOutboxPublisher(configure ?? (_ =>
            {
                _.BatchSize = 50;
                _.MaxRetries = 5;
                _.PollInterval = TimeSpan.FromHours(1);
            }));
        }
        else
        {
            services.Configure(configure ?? (_ =>
            {
                _.BatchSize = 50;
                _.MaxRetries = 5;
                _.PollInterval = TimeSpan.FromHours(1);
            }));
            services.AddScoped<IOutboxWriter, OutboxWriter>();
            services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static SigningJobMessage CreateSampleJobMessage() => new(
        JobId: Guid.CreateVersion7(),
        TenantId: "tenant-outbox-1",
        SignatureId: Guid.CreateVersion7(),
        InputPath: "tenants/tenant-outbox-1/signatures/2026/09/06/sig/input.bin",
        RequestedFormat: SignatureFormat.PAdES,
        RequestedProfile: SignatureProfile.B,
        CreatedAt: DateTimeOffset.Parse("2026-09-06T01:30:00Z"),
        Attempt: 1,
        CorrelationId: "corr-outbox-t032");

    private sealed class FakeSigningJobPublisher : ISigningJobPublisher
    {
        public int FailuresBeforeSuccess { get; set; }

        public List<SigningJobMessage> Published { get; } = [];

        public Task PublishAsync(SigningJobMessage message, CancellationToken cancellationToken = default)
        {
            if (FailuresBeforeSuccess > 0)
            {
                FailuresBeforeSuccess--;
                throw new InvalidOperationException("Simulated transient broker failure.");
            }

            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}
