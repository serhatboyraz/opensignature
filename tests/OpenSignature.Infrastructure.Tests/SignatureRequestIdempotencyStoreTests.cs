using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Persistence;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpenSignature.Infrastructure.Tests;

public sealed class SignatureRequestIdempotencyStoreTests : IAsyncLifetime
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
    public async Task Sequential_duplicate_idempotency_key_returns_same_request_id()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());

        var tenantId = TenantId.Create("tenant-idem-seq");
        const string idempotencyKey = "seq-key-1";

        IdempotentCreateResult first;
        IdempotentCreateResult second;

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISignatureRequestIdempotencyStore>();
            first = await store.GetOrCreateAsync(CreateCandidate(tenantId, "corr-seq-1", idempotencyKey));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISignatureRequestIdempotencyStore>();
            second = await store.GetOrCreateAsync(CreateCandidate(tenantId, "corr-seq-2", idempotencyKey));
        }

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Request.Id, second.Request.Id);
        Assert.Equal(SignatureStatus.Created, second.Request.Status);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            var count = await db.SignatureRequests.CountAsync(
                r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task Concurrent_creates_with_same_key_yield_single_row()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());

        var tenantId = TenantId.Create("tenant-idem-conc");
        const string idempotencyKey = "conc-key-1";
        const int parallelism = 8;

        var tasks = Enumerable.Range(0, parallelism)
            .Select(async i =>
            {
                await using var scope = provider.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<ISignatureRequestIdempotencyStore>();
                return await store.GetOrCreateAsync(
                    CreateCandidate(tenantId, $"corr-conc-{i}", idempotencyKey));
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var distinctIds = results.Select(r => r.Request.Id).Distinct().ToArray();
        Assert.Single(distinctIds);

        Assert.Equal(1, results.Count(r => r.WasCreated));
        Assert.Equal(parallelism - 1, results.Count(r => !r.WasCreated));

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var rowCount = await db.SignatureRequests.CountAsync(
            r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task GetOrCreateSigningJob_prevents_duplicate_jobs_for_same_request()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());

        Guid requestId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISignatureRequestIdempotencyStore>();
            var created = await store.GetOrCreateAsync(
                CreateCandidate(TenantId.Create("tenant-job-1"), "corr-job-1", "job-key-1"));
            Assert.True(created.WasCreated);
            requestId = created.Request.Id;
        }

        const int parallelism = 8;
        var tasks = Enumerable.Range(0, parallelism)
            .Select(async _ =>
            {
                await using var scope = provider.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<ISignatureRequestIdempotencyStore>();
                return await store.GetOrCreateSigningJobAsync(requestId);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var distinctJobIds = results.Select(r => r.Job.Id).Distinct().ToArray();
        Assert.Single(distinctJobIds);
        Assert.All(results, r => Assert.Equal(requestId, r.Job.SignatureRequestId));
        Assert.Equal(1, results.Count(r => r.WasCreated));

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        Assert.Equal(1, await db.SigningJobs.CountAsync(j => j.SignatureRequestId == requestId));
    }

    [Fact]
    public async Task FindByIdempotencyKey_returns_null_when_missing()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISignatureRequestIdempotencyStore>();

        var found = await store.FindByIdempotencyKeyAsync(
            TenantId.Create("tenant-missing"),
            "no-such-key");

        Assert.Null(found);
    }

    private static SignatureRequest CreateCandidate(
        TenantId tenantId,
        string correlationId,
        string? idempotencyKey)
        => SignatureRequest.Create(
            tenantId: tenantId,
            correlationId: CorrelationId.Create(correlationId),
            format: SignatureFormat.PAdES,
            profile: SignatureProfile.B,
            inputFileId: Guid.CreateVersion7(),
            signingProvider: SigningProviderType.Pfx,
            createdBy: "idempotency-tests",
            idempotencyKey: idempotencyKey);

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPersistence(connectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
