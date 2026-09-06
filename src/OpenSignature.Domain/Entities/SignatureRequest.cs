using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Entities;

public sealed class SignatureRequest
{
    private SignatureRequest()
    {
        TenantId = null!;
        CorrelationId = null!;
        CreatedBy = string.Empty;
        Appearance = PadesAppearanceSettings.Create(visible: false);
    }

    private SignatureRequest(
        Guid id,
        TenantId tenantId,
        CorrelationId correlationId,
        SignatureStatus status,
        SignatureFormat format,
        SignatureProfile profile,
        Guid inputFileId,
        Guid? outputFileId,
        Guid? certificateId,
        SigningProviderType signingProvider,
        DateTimeOffset createdAt,
        DateTimeOffset? queuedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset? failedAt,
        int retryCount,
        ErrorCode? errorCode,
        string? errorMessage,
        string createdBy,
        string? idempotencyKey)
    {
        Id = id;
        TenantId = tenantId;
        CorrelationId = correlationId;
        Status = status;
        Format = format;
        Profile = profile;
        InputFileId = inputFileId;
        OutputFileId = outputFileId;
        CertificateId = certificateId;
        SigningProvider = signingProvider;
        CreatedAt = createdAt;
        QueuedAt = queuedAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        FailedAt = failedAt;
        RetryCount = retryCount;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CreatedBy = createdBy;
        IdempotencyKey = idempotencyKey;
        Appearance = PadesAppearanceSettings.Invisible;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public CorrelationId CorrelationId { get; private set; }

    public SignatureStatus Status { get; private set; }

    public SignatureFormat Format { get; private set; }

    public SignatureProfile Profile { get; private set; }

    public Guid InputFileId { get; private set; }

    public Guid? OutputFileId { get; private set; }

    public Guid? CertificateId { get; private set; }

    public SigningProviderType SigningProvider { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? QueuedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public int RetryCount { get; private set; }

    public ErrorCode? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string CreatedBy { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public PadesAppearanceSettings Appearance { get; private set; }

    public static SignatureRequest Create(
        TenantId tenantId,
        CorrelationId correlationId,
        SignatureFormat format,
        SignatureProfile profile,
        Guid inputFileId,
        SigningProviderType signingProvider,
        string createdBy,
        Guid? certificateId = null,
        string? idempotencyKey = null,
        DateTimeOffset? createdAt = null,
        Guid? id = null,
        PadesAppearanceSettings? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(correlationId);

        if (inputFileId == Guid.Empty)
        {
            throw new ArgumentException("Input file ID must not be empty.", nameof(inputFileId));
        }

        if (id is Guid providedId && providedId == Guid.Empty)
        {
            throw new ArgumentException("Signature request ID must not be empty when provided.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(createdBy))
        {
            throw new ArgumentException("CreatedBy must not be empty.", nameof(createdBy));
        }

        string? normalizedIdempotencyKey = null;
        if (idempotencyKey is not null)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new ArgumentException("Idempotency key must not be empty when provided.", nameof(idempotencyKey));
            }

            normalizedIdempotencyKey = idempotencyKey.Trim();
        }

        var request = new SignatureRequest(
            id: id ?? Guid.CreateVersion7(),
            tenantId: tenantId,
            correlationId: correlationId,
            status: SignatureStatus.Created,
            format: format,
            profile: profile,
            inputFileId: inputFileId,
            outputFileId: null,
            certificateId: certificateId,
            signingProvider: signingProvider,
            createdAt: createdAt ?? DateTimeOffset.UtcNow,
            queuedAt: null,
            startedAt: null,
            completedAt: null,
            failedAt: null,
            retryCount: 0,
            errorCode: null,
            errorMessage: null,
            createdBy: createdBy.Trim(),
            idempotencyKey: normalizedIdempotencyKey);
        request.Appearance = appearance ?? PadesAppearanceSettings.Create(visible: false);
        return request;
    }

    public void MarkQueued(DateTimeOffset? queuedAt = null)
    {
        TransitionTo(SignatureStatus.Queued);
        QueuedAt = queuedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkProcessing(DateTimeOffset? startedAt = null)
    {
        TransitionTo(SignatureStatus.Processing);
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkCompleted(Guid outputFileId, DateTimeOffset? completedAt = null)
    {
        if (outputFileId == Guid.Empty)
        {
            throw new ArgumentException("Output file ID must not be empty.", nameof(outputFileId));
        }

        TransitionTo(SignatureStatus.Completed);
        OutputFileId = outputFileId;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
        ClearError();
    }

    public void MarkRejected(ErrorCode errorCode, string? errorMessage = null, DateTimeOffset? failedAt = null)
    {
        ArgumentNullException.ThrowIfNull(errorCode);
        TransitionTo(SignatureStatus.Rejected);
        ErrorCode = errorCode;
        ErrorMessage = NormalizeErrorMessage(errorMessage);
        FailedAt = failedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkRetryScheduled(ErrorCode? errorCode = null, string? errorMessage = null)
    {
        TransitionTo(SignatureStatus.RetryScheduled);
        RetryCount++;
        ErrorCode = errorCode;
        ErrorMessage = NormalizeErrorMessage(errorMessage);
    }

    public void MarkFailed(ErrorCode errorCode, string? errorMessage = null, DateTimeOffset? failedAt = null)
    {
        ArgumentNullException.ThrowIfNull(errorCode);
        TransitionTo(SignatureStatus.Failed);
        ErrorCode = errorCode;
        ErrorMessage = NormalizeErrorMessage(errorMessage);
        FailedAt = failedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkCancelled(DateTimeOffset? cancelledAt = null)
    {
        TransitionTo(SignatureStatus.Cancelled);
        FailedAt = cancelledAt ?? DateTimeOffset.UtcNow;
    }

    public void AssignCertificate(Guid certificateId)
    {
        if (certificateId == Guid.Empty)
        {
            throw new ArgumentException("Certificate ID must not be empty.", nameof(certificateId));
        }

        if (SignatureStatusTransitions.IsTerminal(Status))
        {
            throw new DomainException($"Cannot assign certificate when status is {Status}.");
        }

        CertificateId = certificateId;
    }

    private void TransitionTo(SignatureStatus target)
    {
        SignatureStatusTransitions.EnsureCanTransition(Status, target);
        Status = target;
    }

    private void ClearError()
    {
        ErrorCode = null;
        ErrorMessage = null;
    }

    private static string? NormalizeErrorMessage(string? errorMessage)
        => string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
}
