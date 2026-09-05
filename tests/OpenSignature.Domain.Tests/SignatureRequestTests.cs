using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Tests;

public sealed class SignatureRequestTests
{
    [Fact]
    public void Create_sets_created_status_and_identity()
    {
        var request = CreateRequest();

        Assert.Equal(SignatureStatus.Created, request.Status);
        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(0, request.RetryCount);
        Assert.Null(request.OutputFileId);
    }

    [Fact]
    public void Create_rejects_empty_input_file_id()
    {
        Assert.Throws<ArgumentException>(() => CreateRequest(inputFileId: Guid.Empty));
    }

    [Fact]
    public void MarkQueued_from_created_succeeds()
    {
        var request = CreateRequest();

        request.MarkQueued();

        Assert.Equal(SignatureStatus.Queued, request.Status);
        Assert.NotNull(request.QueuedAt);
    }

    [Fact]
    public void MarkCompleted_from_created_is_rejected()
    {
        var request = CreateRequest();

        Assert.Throws<DomainException>(() => request.MarkCompleted(Guid.CreateVersion7()));
    }

    private static SignatureRequest CreateRequest(Guid? inputFileId = null)
        => SignatureRequest.Create(
            tenantId: TenantId.Create("tenant-001"),
            correlationId: CorrelationId.Create("corr-001"),
            format: SignatureFormat.PAdES,
            profile: SignatureProfile.B,
            inputFileId: inputFileId ?? Guid.CreateVersion7(),
            signingProvider: SigningProviderType.Pfx,
            createdBy: "tests");
}
