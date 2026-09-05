using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Tests;

public sealed class DomainEntityTests
{
    [Fact]
    public void StoredFile_create_and_soft_delete()
    {
        var file = StoredFile.Create(
            storageKey: StorageKey.Create("tenants/t1/signatures/2026/09/05/id/input.bin"),
            originalFileName: "contract.pdf",
            contentType: "application/pdf",
            size: 1024,
            sha256: Sha256Hash.Create(new string('a', 64)));

        Assert.False(file.IsDeleted);
        file.MarkDeleted();
        Assert.True(file.IsDeleted);
    }

    [Fact]
    public void Certificate_rejects_inverted_validity_window()
    {
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(-1);

        Assert.Throws<ArgumentException>(() => Certificate.Create(
            thumbprint: CertificateThumbprint.Create(new string('b', 40)),
            subject: "CN=Test",
            issuer: "CN=Issuer",
            serialNumber: "01",
            notBefore: notBefore,
            notAfter: notAfter,
            providerType: SigningProviderType.Pfx,
            providerReference: "certs/dev.pfx"));
    }

    [Fact]
    public void SigningJob_create_starts_pending()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());

        Assert.Equal(SigningJobStatus.Pending, job.Status);
        Assert.Equal(1, job.Attempt);
    }

    [Fact]
    public void AuditEvent_create_requires_entity_id()
    {
        Assert.Throws<ArgumentException>(() => AuditEvent.Create(
            tenantId: TenantId.Create("tenant-001"),
            entityType: "SignatureRequest",
            entityId: Guid.Empty,
            eventType: "Created",
            actor: "api",
            correlationId: CorrelationId.Create("corr-001")));
    }

    [Fact]
    public void OutboxMessage_process_and_fail_lifecycle()
    {
        var message = OutboxMessage.Create("signature.created", """{"jobId":"1"}""");

        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        message.MarkFailed("broker unavailable");
        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Equal(1, message.RetryCount);
        message.MarkProcessed();
        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        Assert.NotNull(message.PublishedAt);
    }
}
