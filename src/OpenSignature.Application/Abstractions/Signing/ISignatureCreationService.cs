using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Application.Abstractions.Signing;

/// <summary>
/// Application port for cryptographic signature creation (format/profile orchestration).
/// Implemented by OpenSignature.Signing; registered from API/Worker DI.
/// </summary>
public interface ISignatureCreationService
{
    /// <summary>
    /// Signs <paramref name="inputStream"/> using the requested format, profile, and provider.
    /// Does not persist files or update signature request state — callers own storage and lifecycle.
    /// </summary>
    /// <param name="inputStream">Document bytes to sign (position at start).</param>
    /// <param name="format">Requested signature format.</param>
    /// <param name="profile">Requested signature profile (B/T/LT/LTA).</param>
    /// <param name="providerType">Signing provider category.</param>
    /// <param name="certificateSelector">Optional certificate selection criteria; null lets the implementation choose a default when allowed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="appearance">Optional visible PAdES appearance. Null means invisible. Non-PAdES formats reject a visible appearance.</param>
    /// <returns>Signed document content and suggested content type.</returns>
    Task<SignatureCreationResult> SignAsync(
        Stream inputStream,
        SignatureFormat format,
        SignatureProfile profile,
        SigningProviderType providerType,
        SigningCertificateSelector? certificateSelector,
        CancellationToken cancellationToken = default,
        SignatureAppearanceOptions? appearance = null);
}
