using OpenSignature.Application.Verification;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Abstractions.Verification;

/// <summary>
/// Verifies already-signed documents. Never performs cryptographic signing.
/// </summary>
public interface ISignatureVerificationService
{
    /// <summary>
    /// Verifies the stored signed output of a completed signature request.
    /// </summary>
    Task<SignatureVerificationOutcome> VerifyStoredAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an uploaded signed artifact in memory (not persisted).
    /// </summary>
    Task<SignatureVerificationOutcome> VerifyUploadedAsync(
        VerifyUploadedSignatureCommand command,
        CancellationToken cancellationToken = default);
}
