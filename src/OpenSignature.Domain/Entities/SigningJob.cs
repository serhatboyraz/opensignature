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

    /// <summary>
    /// Returns <c>true</c> when <see cref="LockedUntil"/> is missing or not after <paramref name="utcNow"/>.
    /// Expired (or missing) locks may be reclaimed after a worker crash or restart.
    /// </summary>
    public bool IsLockExpired(DateTimeOffset utcNow) =>
        LockedUntil is null || LockedUntil <= utcNow;

    /// <summary>
    /// Returns <c>true</c> when this job may be locked: Pending/Failed, or Locked/Processing with an expired lock.
    /// Active (non-expired) Locked/Processing jobs must not be stolen by another worker.
    /// </summary>
    public bool CanAcquireOrReclaim(DateTimeOffset utcNow) =>
        Status is SigningJobStatus.Pending or SigningJobStatus.Failed
        || ((Status is SigningJobStatus.Locked or SigningJobStatus.Processing) && IsLockExpired(utcNow));

    /// <summary>
    /// Transitions Pending or Failed → Locked. Prefer <see cref="AcquireOrReclaimLock"/> when reclaiming expired locks.
    /// </summary>
    public void AcquireLock(DateTimeOffset lockedUntil, DateTimeOffset? startedAt = null)
    {
        if (Status is not (SigningJobStatus.Pending or SigningJobStatus.Failed))
        {
            throw new DomainException($"Cannot lock signing job in status {Status}.");
        }

        ApplyLock(lockedUntil, startedAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Acquires a lock from Pending/Failed, or reclaims an expired Locked/Processing lock after worker crash/restart.
    /// </summary>
    public void AcquireOrReclaimLock(
        DateTimeOffset lockedUntil,
        DateTimeOffset? utcNow = null,
        DateTimeOffset? startedAt = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        if (!CanAcquireOrReclaim(now))
        {
            throw new DomainException(
                $"Cannot acquire or reclaim signing job lock in status {Status} with LockedUntil {LockedUntil}.");
        }

        ApplyLock(lockedUntil, startedAt ?? now);
    }

    private void ApplyLock(DateTimeOffset lockedUntil, DateTimeOffset startedAt)
    {
        Status = SigningJobStatus.Locked;
        LockedUntil = lockedUntil;
        StartedAt = startedAt;
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

    /// <summary>
    /// Drops an active lock after a retryable failure so the next delivery can acquire it.
    /// Does not record <see cref="CompletedAt"/> — the job is not terminal.
    /// </summary>
    public void ReleaseLockForRetry(string? lastError = null)
    {
        if (Status is not (SigningJobStatus.Locked or SigningJobStatus.Processing))
        {
            throw new DomainException($"Cannot release signing job lock for retry in status {Status}.");
        }

        Status = SigningJobStatus.Failed;
        LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim();
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
