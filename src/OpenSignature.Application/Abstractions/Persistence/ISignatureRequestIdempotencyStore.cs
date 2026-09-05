using OpenSignature.Domain.Entities;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Abstractions.Persistence;

/// <summary>
/// Looks up and creates signature requests and signing jobs with tenant-scoped idempotency.
/// </summary>
public interface ISignatureRequestIdempotencyStore
{
    /// <summary>
    /// Finds an existing signature request for <paramref name="tenantId"/> and <paramref name="idempotencyKey"/>.
    /// </summary>
    /// <returns>The matching request, or <c>null</c> when none exists.</returns>
    Task<SignatureRequest?> FindByIdempotencyKeyAsync(
        TenantId tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing request for the candidate's tenant + idempotency key, or persists
    /// <paramref name="candidate"/> when none exists. Concurrent callers with the same key
    /// observe a single final row (unique constraint + conflict recovery).
    /// </summary>
    /// <remarks>
    /// When <paramref name="candidate"/> has a null idempotency key, the candidate is always inserted.
    /// </remarks>
    Task<IdempotentCreateResult> GetOrCreateAsync(
        SignatureRequest candidate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing signing job for <paramref name="signatureRequestId"/>, or creates a pending one.
    /// Concurrent callers observe a single final row per signature request.
    /// </summary>
    Task<IdempotentSigningJobResult> GetOrCreateSigningJobAsync(
        Guid signatureRequestId,
        CancellationToken cancellationToken = default);
}
