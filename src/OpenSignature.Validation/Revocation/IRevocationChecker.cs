using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Revocation;

/// <summary>
/// Pluggable revocation checker. Implementations may use OCSP, CRL, stubs, or test doubles.
/// Private keys are never required or exposed.
/// </summary>
public interface IRevocationChecker
{
    /// <summary>
    /// Checks whether <paramref name="certificate"/> is revoked as of <paramref name="asOf"/>.
    /// </summary>
    Task<RevocationCheckResult> CheckAsync(
        X509Certificate2 certificate,
        X509Certificate2? issuer,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}
