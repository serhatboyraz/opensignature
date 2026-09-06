using Microsoft.EntityFrameworkCore;
using OpenSignature.Application.Abstractions.Persistence;
using OpenSignature.Domain.Enums;

namespace OpenSignature.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL / EF Core atomic lock acquire via conditional <c>UPDATE</c>.
/// Only one concurrent caller wins; expired Locked/Processing rows may be reclaimed after crash/restart.
/// </summary>
public sealed class EfSigningJobLockService : ISigningJobLockService
{
    private readonly OpenSignatureDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfSigningJobLockService(OpenSignatureDbContext db, TimeProvider timeProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(
        Guid jobId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID must not be empty.", nameof(jobId));
        }

        if (lockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockDuration), lockDuration, "Lock duration must be positive.");
        }

        var now = _timeProvider.GetUtcNow();
        var lockedUntil = now.Add(lockDuration);

        // Atomic conditional UPDATE: Pending/Failed, or Locked/Processing with expired/missing lock.
        var updated = await _db.SigningJobs
            .Where(j => j.Id == jobId
                        && (j.Status == SigningJobStatus.Pending
                            || j.Status == SigningJobStatus.Failed
                            || ((j.Status == SigningJobStatus.Locked || j.Status == SigningJobStatus.Processing)
                                && (j.LockedUntil == null || j.LockedUntil <= now))))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, SigningJobStatus.Locked)
                    .SetProperty(j => j.LockedUntil, lockedUntil)
                    .SetProperty(j => j.StartedAt, now)
                    .SetProperty(j => j.LastError, (string?)null),
                cancellationToken)
            .ConfigureAwait(false);

        return updated == 1;
    }
}
