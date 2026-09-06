using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;

namespace OpenSignature.Domain.Tests;

public sealed class SigningJobLockTests
{
    [Fact]
    public void AcquireLock_from_pending_sets_locked_until()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        var until = DateTimeOffset.UtcNow.AddMinutes(15);

        job.AcquireLock(until);

        Assert.Equal(SigningJobStatus.Locked, job.Status);
        Assert.Equal(until, job.LockedUntil);
        Assert.NotNull(job.StartedAt);
    }

    [Fact]
    public void CanAcquireOrReclaim_false_while_lock_active()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        job.AcquireLock(now.AddMinutes(15), startedAt: now);
        job.MarkProcessing();

        Assert.False(job.CanAcquireOrReclaim(now));
        Assert.False(job.IsLockExpired(now));
    }

    [Fact]
    public void AcquireOrReclaimLock_reclaims_expired_processing()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        var started = DateTimeOffset.UtcNow.AddMinutes(-20);
        job.AcquireLock(started.AddMinutes(5), startedAt: started);
        job.MarkProcessing();

        var now = DateTimeOffset.UtcNow;
        Assert.True(job.IsLockExpired(now));
        Assert.True(job.CanAcquireOrReclaim(now));

        var newUntil = now.AddMinutes(15);
        job.AcquireOrReclaimLock(newUntil, utcNow: now);

        Assert.Equal(SigningJobStatus.Locked, job.Status);
        Assert.Equal(newUntil, job.LockedUntil);
    }

    [Fact]
    public void AcquireOrReclaimLock_reclaims_expired_locked()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        job.AcquireLock(started.AddMinutes(1), startedAt: started);

        var now = DateTimeOffset.UtcNow;
        job.AcquireOrReclaimLock(now.AddMinutes(15), utcNow: now);

        Assert.Equal(SigningJobStatus.Locked, job.Status);
        Assert.Equal(now.AddMinutes(15), job.LockedUntil);
    }

    [Fact]
    public void AcquireOrReclaimLock_throws_when_active_lock_held()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        job.AcquireLock(now.AddMinutes(15), startedAt: now);

        Assert.Throws<DomainException>(() =>
            job.AcquireOrReclaimLock(now.AddMinutes(15), utcNow: now));
    }

    [Fact]
    public void AcquireOrReclaimLock_allows_failed_retry()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        job.AcquireLock(DateTimeOffset.UtcNow.AddMinutes(15));
        job.MarkFailed("transient");

        var now = DateTimeOffset.UtcNow;
        Assert.True(job.CanAcquireOrReclaim(now));
        job.AcquireOrReclaimLock(now.AddMinutes(15), utcNow: now);

        Assert.Equal(SigningJobStatus.Locked, job.Status);
        Assert.Null(job.LastError);
    }

    [Fact]
    public void ReleaseLockForRetry_clears_lock_without_completing()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        job.AcquireLock(now.AddMinutes(15), startedAt: now);
        job.MarkProcessing();

        job.ReleaseLockForRetry("Expected 'obj' at offset 300.");

        Assert.Equal(SigningJobStatus.Failed, job.Status);
        Assert.Null(job.LockedUntil);
        Assert.Null(job.CompletedAt);
        Assert.Equal("Expected 'obj' at offset 300.", job.LastError);
        Assert.True(job.CanAcquireOrReclaim(now));
    }

    [Fact]
    public void ReleaseLockForRetry_throws_when_not_locked_or_processing()
    {
        var job = SigningJob.Create(Guid.CreateVersion7());
        Assert.Throws<DomainException>(() => job.ReleaseLockForRetry("no lock"));
    }
}
