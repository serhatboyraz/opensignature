using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;

namespace OpenSignature.Domain.Entities;

public sealed class SigningJob
{
    private SigningJob(
        Guid id,
        Guid signatureRequestId,
        SigningJobStatus status,
        int attempt,
        string? lastError,
        DateTimeOffset? lockedUntil,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt)
    {
        Id = id;
        SignatureRequestId = signatureRequestId;
        Status = status;
        Attempt = attempt;
        LastError = lastError;
        LockedUntil = lockedUntil;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public Guid Id { get; private set; }

    public Guid SignatureRequestId { get; private set; }

    public SigningJobStatus Status { get; private set; }

    public int Attempt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static SigningJob Create(
        Guid signatureRequestId,
        int attempt = 1,
        DateTimeOffset? createdAt = null)
    {
        if (signatureRequestId == Guid.Empty)
        {
            throw new ArgumentException("Signature request ID must not be empty.", nameof(signatureRequestId));
        }

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be at least 1.");
        }

        return new SigningJob(
            id: Guid.CreateVersion7(),
            signatureRequestId: signatureRequestId,
            status: SigningJobStatus.Pending,
            attempt: attempt,
            lastError: null,
            lockedUntil: null,
            createdAt: createdAt ?? DateTimeOffset.UtcNow,
            startedAt: null,
            completedAt: null);
    }

    public void AcquireLock(DateTimeOffset lockedUntil, DateTimeOffset? startedAt = null)
    {
        if (Status is not (SigningJobStatus.Pending or SigningJobStatus.Failed))
        {
            throw new DomainException($"Cannot lock signing job in status {Status}.");
        }

        Status = SigningJobStatus.Locked;
        LockedUntil = lockedUntil;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
        LastError = null;
    }

    public void MarkProcessing()
    {
        if (Status != SigningJobStatus.Locked)
        {
            throw new DomainException($"Cannot process signing job in status {Status}.");
        }

        Status = SigningJobStatus.Processing;
    }

    public void MarkCompleted(DateTimeOffset? completedAt = null)
    {
        if (Status is not (SigningJobStatus.Locked or SigningJobStatus.Processing))
        {
            throw new DomainException($"Cannot complete signing job in status {Status}.");
        }

        Status = SigningJobStatus.Completed;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
        LockedUntil = null;
        LastError = null;
    }

    public void MarkFailed(string? lastError = null, DateTimeOffset? completedAt = null)
    {
        if (Status is not (SigningJobStatus.Locked or SigningJobStatus.Processing))
        {
            throw new DomainException($"Cannot fail signing job in status {Status}.");
        }

        Status = SigningJobStatus.Failed;
        LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim();
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
        LockedUntil = null;
    }

    public void MarkCancelled(DateTimeOffset? completedAt = null)
    {
        if (Status is SigningJobStatus.Completed or SigningJobStatus.Cancelled)
        {
            throw new DomainException($"Cannot cancel signing job in status {Status}.");
        }

        Status = SigningJobStatus.Cancelled;
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
        LockedUntil = null;
    }
}
