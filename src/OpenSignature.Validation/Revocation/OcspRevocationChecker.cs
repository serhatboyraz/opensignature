using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Revocation;

/// <summary>
/// OCSP-oriented revocation checker.
/// MVP limitation: does not claim full ETSI EN 319 102-1 OCSP conformance.
/// When AIA OCSP URLs are absent or unreachable, returns <see cref="RevocationStatus.Unknown"/>.
/// Prefer injecting a stub/mock in tests; live responders are not required for MVP.
/// </summary>
public sealed class OcspRevocationChecker : IRevocationChecker
{
    public const string SourceName = "OCSP";

    public Task<RevocationCheckResult> CheckAsync(
        X509Certificate2 certificate,
        X509Certificate2? issuer,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        cancellationToken.ThrowIfCancellationRequested();

        // MVP: inspect AIA for OCSP URIs but do not perform live HTTP by default.
        // A future enhancement can fetch and verify OCSP responses against the issuer.
        var hasOcspUri = certificate.Extensions
            .OfType<X509Extension>()
            .Any(static e => e.Oid?.Value == "1.3.6.1.5.5.7.1.1"
                && e.Format(false).Contains("OCSP", StringComparison.OrdinalIgnoreCase));

        if (!hasOcspUri)
        {
            return Task.FromResult(new RevocationCheckResult(
                RevocationStatus.Unknown,
                SourceName,
                "Certificate does not advertise an OCSP responder URI."));
        }

        return Task.FromResult(new RevocationCheckResult(
            RevocationStatus.Unknown,
            SourceName,
            "OCSP URI present but live OCSP fetch is not enabled in this MVP build."));
    }
}
