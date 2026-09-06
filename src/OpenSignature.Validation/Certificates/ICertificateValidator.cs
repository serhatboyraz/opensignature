using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Certificates;

/// <summary>
/// Validates X.509 certificates (public material only) per PRODUCT-SPEC §20 pipeline.
/// Does not claim full ETSI EN 319 102-1 certificate path validation conformance.
/// </summary>
public interface ICertificateValidator
{
    /// <summary>Validates a loaded public certificate.</summary>
    Task<CertificateValidationResult> ValidateAsync(
        X509Certificate2 certificate,
        CertificateValidationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Validates a certificate from public DER bytes.</summary>
    Task<CertificateValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> publicCertificateDer,
        CertificateValidationOptions? options = null,
        CancellationToken cancellationToken = default);
}
