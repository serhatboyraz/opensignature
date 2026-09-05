using OpenSignature.Application.Signatures;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Abstractions.Signatures;

/// <summary>
/// Application use cases for asynchronous signature requests (API surface).
/// Never performs cryptographic signing synchronously.
/// </summary>
public interface ISignatureRequestService
{
    /// <summary>
    /// Validates, stores the input, persists request/job metadata, and enqueues an outbox signing job.
    /// Returns immediately with a queued (or existing idempotent) request.
    /// </summary>
    Task<CreateSignatureResult> CreateAsync(
        CreateSignatureCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns signature request status for the tenant, or <c>null</c> when not found.
    /// </summary>
    Task<SignatureRequestStatusDto?> GetAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the signed content stream when the request is Completed; otherwise returns a failure reason.
    /// </summary>
    Task<SignatureContentResult> GetContentAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a request that has not yet reached a terminal or non-cancellable state.
    /// </summary>
    Task<CancelSignatureResult> CancelAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default);
}
