using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpenSignature.Infrastructure.Tests;

public sealed class PostgreSqlPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("opensignature")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migrations_apply_and_entities_round_trip()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();

        await db.Database.MigrateAsync();

        var tenantId = TenantId.Create("tenant-persistence-1");
        var correlationId = CorrelationId.Create("corr-persistence-1");
        var inputFile = StoredFile.Create(
            storageKey: StorageKey.Create("tenants/tenant-persistence-1/signatures/2026/09/06/req/input.bin"),
            originalFileName: "contract.pdf",
            contentType: "application/pdf",
            size: 2048,
            sha256: Sha256Hash.Create(new string('a', 64)));

        var certificate = Certificate.Create(
            thumbprint: CertificateThumbprint.Create(new string('b', 40)),
            subject: "CN=OpenSignature Test",
            issuer: "CN=OpenSignature Test CA",
            serialNumber: "01",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            providerType: SigningProviderType.Pfx,
            providerReference: "certs/dev.pfx");

        var request = SignatureRequest.Create(
            tenantId: tenantId,
            correlationId: correlationId,
            format: SignatureFormat.PAdES,
            profile: SignatureProfile.B,
            inputFileId: inputFile.Id,
            signingProvider: SigningProviderType.Pfx,
            createdBy: "persistence-tests",
            certificateId: certificate.Id,
            idempotencyKey: "idem-key-1");

        var job = SigningJob.Create(request.Id);
        var audit = AuditEvent.Create(
            tenantId: tenantId,
            entityType: nameof(SignatureRequest),
            entityId: request.Id,
            eventType: "Created",
            actor: "persistence-tests",
            correlationId: correlationId,
            metadata: """{"source":"test"}""");
        var outbox = OutboxMessage.Create(
            type: "signature.created",
            payload: $$"""{"signatureRequestId":"{{request.Id}}"}""");

        db.StoredFiles.Add(inputFile);
        db.Certificates.Add(certificate);
        db.SignatureRequests.Add(request);
        db.SigningJobs.Add(job);
        db.AuditEvents.Add(audit);
        db.OutboxMessages.Add(outbox);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var loadedRequest = await db.SignatureRequests.SingleAsync(r => r.Id == request.Id);
        var loadedJob = await db.SigningJobs.SingleAsync(j => j.Id == job.Id);
        var loadedFile = await db.StoredFiles.SingleAsync(f => f.Id == inputFile.Id);
        var loadedCertificate = await db.Certificates.SingleAsync(c => c.Id == certificate.Id);
        var loadedAudit = await db.AuditEvents.SingleAsync(a => a.Id == audit.Id);
        var loadedOutbox = await db.OutboxMessages.SingleAsync(o => o.Id == outbox.Id);

        Assert.Equal(tenantId, loadedRequest.TenantId);
        Assert.Equal(correlationId, loadedRequest.CorrelationId);
        Assert.Equal(SignatureStatus.Created, loadedRequest.Status);
        Assert.Equal("idem-key-1", loadedRequest.IdempotencyKey);
        Assert.Equal(certificate.Id, loadedRequest.CertificateId);

        Assert.Equal(request.Id, loadedJob.SignatureRequestId);
        Assert.Equal(SigningJobStatus.Pending, loadedJob.Status);

        Assert.Equal(inputFile.StorageKey, loadedFile.StorageKey);
        Assert.Equal(inputFile.Sha256, loadedFile.Sha256);

        Assert.Equal(certificate.Thumbprint, loadedCertificate.Thumbprint);
        Assert.Equal(SigningProviderType.Pfx, loadedCertificate.ProviderType);

        Assert.Equal(tenantId, loadedAudit.TenantId);
        Assert.Equal(correlationId, loadedAudit.CorrelationId);
        Assert.Equal("""{"source":"test"}""", loadedAudit.Metadata);

        Assert.Equal(OutboxMessageStatus.Pending, loadedOutbox.Status);
        Assert.Contains(request.Id.ToString(), loadedOutbox.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idempotency_key_is_unique_per_tenant_when_present()
    {
        await using var provider = BuildProvider(_postgres.GetConnectionString());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = TenantId.Create("tenant-idem-1");
        var inputFileId = Guid.CreateVersion7();

        var first = SignatureRequest.Create(
            tenantId: tenantId,
            correlationId: CorrelationId.Create("corr-idem-1"),
            format: SignatureFormat.PAdES,
            profile: SignatureProfile.B,
            inputFileId: inputFileId,
            signingProvider: SigningProviderType.Pfx,
            createdBy: "persistence-tests",
            idempotencyKey: "shared-key");

        var duplicate = SignatureRequest.Create(
            tenantId: tenantId,
            correlationId: CorrelationId.Create("corr-idem-2"),
            format: SignatureFormat.PAdES,
            profile: SignatureProfile.B,
            inputFileId: Guid.CreateVersion7(),
            signingProvider: SigningProviderType.Pfx,
            createdBy: "persistence-tests",
            idempotencyKey: "shared-key");

        db.SignatureRequests.Add(first);
        await db.SaveChangesAsync();

        db.SignatureRequests.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPersistence(connectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
