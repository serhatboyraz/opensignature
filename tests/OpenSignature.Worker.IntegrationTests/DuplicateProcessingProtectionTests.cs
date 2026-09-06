using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Application.Messages;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Signing.Contracts;
using OpenSignature.Worker.Messaging;
using Testcontainers.PostgreSql;

namespace OpenSignature.Worker.IntegrationTests;

public sealed class DuplicateProcessingProtectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("opensignature")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "opensignature-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicate_message_completes_signing_only_once()
    {
        await using var provider = BuildProvider();
        var seed = await SeedQueuedJobAsync(provider, "tenant-dup-seq");
        var signer = (CountingSignatureCreationService)provider.GetRequiredService<ISignatureCreationService>();

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ISigningJobProcessor>();
            await processor.ProcessAsync(seed.Message);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ISigningJobProcessor>();
            await processor.ProcessAsync(seed.Message);
        }

        Assert.Equal(1, signer.CallCount);

        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var request = await db.SignatureRequests.SingleAsync(r => r.Id == seed.Message.SignatureId);
        var job = await db.SigningJobs.SingleAsync(j => j.Id == seed.Message.JobId);
        Assert.Equal(SignatureStatus.Completed, request.Status);
        Assert.Equal(SigningJobStatus.Completed, job.Status);
        Assert.NotNull(request.OutputFileId);

        var storage = verify.ServiceProvider.GetRequiredService<IFileStorage>();
        var outputKey = SignatureStorageKeys.ForSigned(
            TenantId.Create(seed.Message.TenantId),
            seed.Message.SignatureId,
            seed.CreatedAt);
        Assert.True(await storage.ExistsAsync(outputKey));
    }

    [Fact]
    public async Task Concurrent_duplicate_messages_only_one_signs()
    {
        await using var provider = BuildProvider();
        var seed = await SeedQueuedJobAsync(provider, "tenant-dup-conc");
        var signer = (CountingSignatureCreationService)provider.GetRequiredService<ISignatureCreationService>();
        signer.Delay = TimeSpan.FromMilliseconds(200);

        const int parallelism = 6;
        var tasks = Enumerable.Range(0, parallelism)
            .Select(async _ =>
            {
                await using var scope = provider.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ISigningJobProcessor>();
                await processor.ProcessAsync(seed.Message);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, signer.CallCount);

        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var request = await db.SignatureRequests.SingleAsync(r => r.Id == seed.Message.SignatureId);
        var job = await db.SigningJobs.SingleAsync(j => j.Id == seed.Message.JobId);
        Assert.Equal(SignatureStatus.Completed, request.Status);
        Assert.Equal(SigningJobStatus.Completed, job.Status);
    }

    private async Task<SeededJob> SeedQueuedJobAsync(ServiceProvider provider, string tenant)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var createdAt = DateTimeOffset.UtcNow;
        var signatureId = Guid.CreateVersion7();
        var tenantId = TenantId.Create(tenant);
        var inputKey = SignatureStorageKeys.ForInput(tenantId, signatureId, createdAt);

        var inputBytes = Encoding.UTF8.GetBytes("duplicate-protection-input");
        await using (var input = new MemoryStream(inputBytes))
        {
            await storage.SaveAsync(inputKey, input, "application/octet-stream");
        }

        var sha = Convert.ToHexString(SHA256.HashData(inputBytes)).ToLowerInvariant();
        var inputFile = StoredFile.Create(
            storageKey: inputKey,
            originalFileName: "input.bin",
            contentType: "application/octet-stream",
            size: inputBytes.Length,
            sha256: Sha256Hash.Create(sha),
            id: Guid.CreateVersion7(),
            createdAt: createdAt);

        var request = SignatureRequest.Create(
            tenantId: tenantId,
            correlationId: CorrelationId.Create($"corr-{tenant}"),
            format: SignatureFormat.CAdES,
            profile: SignatureProfile.B,
            inputFileId: inputFile.Id,
            signingProvider: SigningProviderType.Pfx,
            createdBy: "dup-tests",
            createdAt: createdAt,
            id: signatureId);

        request.MarkQueued(createdAt);
        var job = SigningJob.Create(request.Id, createdAt: createdAt);

        db.StoredFiles.Add(inputFile);
        db.SignatureRequests.Add(request);
        db.SigningJobs.Add(job);
        await db.SaveChangesAsync();

        var message = new SigningJobMessage(
            JobId: job.Id,
            TenantId: tenant,
            SignatureId: signatureId,
            InputPath: inputKey.Value,
            RequestedFormat: SignatureFormat.CAdES,
            RequestedProfile: SignatureProfile.B,
            CreatedAt: createdAt,
            Attempt: 1,
            CorrelationId: request.CorrelationId.Value);

        return new SeededJob(message, createdAt);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddPersistence(_postgres.GetConnectionString());
        services.AddLocalFileStorage(options => options.RootPath = _storageRoot);
        services.AddSingleton<ISignatureCreationService, CountingSignatureCreationService>();
        services.AddScoped<ISigningJobProcessor, SignatureSigningJobProcessor>();
        services.AddLogging();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed record SeededJob(SigningJobMessage Message, DateTimeOffset CreatedAt);

    private sealed class CountingSignatureCreationService : ISignatureCreationService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public async Task<SignatureCreationResult> SignAsync(
            Stream inputStream,
            SignatureFormat format,
            SignatureProfile profile,
            SigningProviderType providerType,
            SigningCertificateSelector? certificateSelector,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            await inputStream.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
            var signed = new MemoryStream(Encoding.UTF8.GetBytes("signed-output"));
            return new SignatureCreationResult(
                signed,
                "application/octet-stream",
                format,
                profile,
                "pfx",
                new string('a', 40));
        }
    }
}
