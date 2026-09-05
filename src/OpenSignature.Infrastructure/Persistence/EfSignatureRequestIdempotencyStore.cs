using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenSignature.Application.Abstractions.Persistence;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Infrastructure.Persistence;

/// <summary>
/// EF Core / PostgreSQL implementation of <see cref="ISignatureRequestIdempotencyStore"/>.
/// </summary>
public sealed class EfSignatureRequestIdempotencyStore : ISignatureRequestIdempotencyStore
{
    private readonly OpenSignatureDbContext _db;

    /// <summary>
    /// Creates a store backed by <paramref name="db"/>.
    /// </summary>
    public EfSignatureRequestIdempotencyStore(OpenSignatureDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<SignatureRequest?> FindByIdempotencyKeyAsync(
        TenantId tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);

        return await _db.SignatureRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.TenantId == tenantId && r.IdempotencyKey == normalizedKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IdempotentCreateResult> GetOrCreateAsync(
        SignatureRequest candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.IdempotencyKey is null)
        {
            _db.SignatureRequests.Add(candidate);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new IdempotentCreateResult(candidate, WasCreated: true);
        }

        var existing = await FindByIdempotencyKeyAsync(
                candidate.TenantId,
                candidate.IdempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new IdempotentCreateResult(existing, WasCreated: false);
        }

        _db.SignatureRequests.Add(candidate);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new IdempotentCreateResult(candidate, WasCreated: true);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            Detach(candidate);

            existing = await FindByIdempotencyKeyAsync(
                    candidate.TenantId,
                    candidate.IdempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                throw new InvalidOperationException(
                    "Unique constraint conflict on signature request idempotency key, but no existing row was found.",
                    ex);
            }

            return new IdempotentCreateResult(existing, WasCreated: false);
        }
    }

    /// <inheritdoc />
    public async Task<IdempotentSigningJobResult> GetOrCreateSigningJobAsync(
        Guid signatureRequestId,
        CancellationToken cancellationToken = default)
    {
        if (signatureRequestId == Guid.Empty)
        {
            throw new ArgumentException("Signature request ID must not be empty.", nameof(signatureRequestId));
        }

        var existing = await FindSigningJobAsync(signatureRequestId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new IdempotentSigningJobResult(existing, WasCreated: false);
        }

        var job = SigningJob.Create(signatureRequestId);
        _db.SigningJobs.Add(job);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new IdempotentSigningJobResult(job, WasCreated: true);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            Detach(job);

            existing = await FindSigningJobAsync(signatureRequestId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                throw new InvalidOperationException(
                    "Unique constraint conflict on signing job signature request id, but no existing row was found.",
                    ex);
            }

            return new IdempotentSigningJobResult(existing, WasCreated: false);
        }
    }

    private async Task<SigningJob?> FindSigningJobAsync(
        Guid signatureRequestId,
        CancellationToken cancellationToken)
    {
        return await _db.SigningJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(j => j.SignatureRequestId == signatureRequestId, cancellationToken)
            .ConfigureAwait(false);
    }

    private void Detach(object entity)
    {
        var entry = _db.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key must not be empty.", nameof(idempotencyKey));
        }

        return idempotencyKey.Trim();
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
