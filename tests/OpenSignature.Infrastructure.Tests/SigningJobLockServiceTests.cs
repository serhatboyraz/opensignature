using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Persistence;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpenSignature.Infrastructure.Tests;

public sealed class SigningJobLockServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("opensignature")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var provider = BuildProvider(_postgres.GetConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task TryAcquire_from_pending_succeeds_once()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        var jobId = await SeedPendingJobAsync(provider, "tenant-lock-seq");

        await using var scope = provider.CreateAsyncScope();
        var locks = scope.ServiceProvider.GetRequiredService<ISigningJobLockService>();

        Assert.True(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));
        Assert.False(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));

        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var job = await db.SigningJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal(SigningJobStatus.Locked, job.Status);
        Assert.NotNull(job.LockedUntil);
        Assert.True(job.LockedUntil > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Concurrent_TryAcquire_only_one_wins()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        var jobId = await SeedPendingJobAsync(provider, "tenant-lock-conc");

        const int parallelism = 12;
        var tasks = Enumerable.Range(0, parallelism)
            .Select(async _ =>
            {
                await using var scope = provider.CreateAsyncScope();
                var locks = scope.ServiceProvider.GetRequiredService<ISigningJobLockService>();
                return await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15));
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(acquired => acquired));
        Assert.Equal(parallelism - 1, results.Count(acquired => !acquired));

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var job = await db.SigningJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal(SigningJobStatus.Locked, job.Status);
    }

    [Fact]
    public async Task Expired_processing_lock_can_be_reclaimed()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        var jobId = await SeedPendingJobAsync(provider, "tenant-lock-reclaim");

        await using (var scope = provider.CreateAsyncScope())
        {
            var locks = scope.ServiceProvider.GetRequiredService<ISigningJobLockService>();
            Assert.True(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));

            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var job = await db.SigningJobs.SingleAsync(j => j.Id == jobId);
            job.MarkProcessing();
            await db.SaveChangesAsync();

            // Simulate crash: expire the lock while still Processing.
            await db.SigningJobs
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.LockedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var locks = scope.ServiceProvider.GetRequiredService<ISigningJobLockService>();
            Assert.True(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));

            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var job = await db.SigningJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            Assert.Equal(SigningJobStatus.Locked, job.Status);
            Assert.True(job.LockedUntil > DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task Active_processing_lock_is_not_stolen()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        var jobId = await SeedPendingJobAsync(provider, "tenant-lock-active");

        await using var scope = provider.CreateAsyncScope();
        var locks = scope.ServiceProvider.GetRequiredService<ISigningJobLockService>();
        Assert.True(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));

        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var job = await db.SigningJobs.SingleAsync(j => j.Id == jobId);
        job.MarkProcessing();
        await db.SaveChangesAsync();

        Assert.False(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));

        var reloaded = await db.SigningJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal(SigningJobStatus.Processing, reloaded.Status);
    }

    [Fact]
    public async Task Completed_job_cannot_be_acquired()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        var jobId = await SeedPendingJobAsync(provider, "tenant-lock-done");

        await using var scope = provider.CreateAsyncScope();
        var locks = scope.ServiceProvider.GetRequiredService<ISigningJobLockService>();
        Assert.True(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));

        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var job = await db.SigningJobs.SingleAsync(j => j.Id == jobId);
        job.MarkProcessing();
        job.MarkCompleted();
        await db.SaveChangesAsync();

        Assert.False(await locks.TryAcquireAsync(jobId, TimeSpan.FromMinutes(15)));
    }

    private static async Task<Guid> SeedPendingJobAsync(ServiceProvider provider, string tenant)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();

        var inputFile = StoredFile.Create(
            storageKey: StorageKey.Create($"tenants/{tenant}/signatures/2026/09/06/req/input.bin"),
            originalFileName: "doc.pdf",
            contentType: "application/pdf",
            size: 128,
            sha256: Sha256Hash.Create(new string('a', 64)));

        var request = SignatureRequest.Create(
            tenantId: TenantId.Create(tenant),
            correlationId: CorrelationId.Create($"corr-{tenant}"),
            format: SignatureFormat.CAdES,
            profile: SignatureProfile.B,
            inputFileId: inputFile.Id,
            signingProvider: SigningProviderType.Pfx,
            createdBy: "lock-tests");

        request.MarkQueued();
        var job = SigningJob.Create(request.Id);

        db.StoredFiles.Add(inputFile);
        db.SignatureRequests.Add(request);
        db.SigningJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPersistence(connectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
