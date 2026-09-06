namespace OpenSignature.Application.Abstractions.Persistence;

/// <summary>
/// Atomically acquires (or reclaims an expired) signing-job processing lock.
/// Used by workers so at-least-once RabbitMQ delivery and concurrent consumers do not double-sign.
/// </summary>
public interface ISigningJobLockService
{
    /// <summary>
    /// Attempts to transition the job to <c>Locked</c> when it is Pending/Failed,
    /// or Locked/Processing with an expired (or missing) <c>LockedUntil</c>.
    /// </summary>
    /// <param name="jobId">Signing job identifier.</param>
    /// <param name="lockDuration">How long the lock is held before another worker may reclaim it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when this caller acquired the lock; <c>false</c> when another worker holds an active lock
    /// or the job is in a terminal / non-acquirable state (caller should ACK without signing).
    /// </returns>
    Task<bool> TryAcquireAsync(
        Guid jobId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);
}
