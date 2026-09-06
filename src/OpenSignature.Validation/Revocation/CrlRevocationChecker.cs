using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Revocation;

/// <summary>
/// CRL-oriented revocation checker (fallback after OCSP).
/// MVP limitation: does not claim full ETSI CRL processing / delta-CRL conformance.
/// When CDP URLs are absent or unreachable, returns <see cref="RevocationStatus.Unknown"/>.
/// </summary>
public sealed class CrlRevocationChecker : IRevocationChecker
{
    public const string SourceName = "CRL";

    public Task<RevocationCheckResult> CheckAsync(
        X509Certificate2 certificate,
        X509Certificate2? issuer,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        cancellationToken.ThrowIfCancellationRequested();

        var hasCdp = certificate.Extensions
            .OfType<X509Extension>()
            .Any(static e => e.Oid?.Value == "2.5.29.31");

        if (!hasCdp)
        {
            return Task.FromResult(new RevocationCheckResult(
                RevocationStatus.Unknown,
                SourceName,
                "Certificate does not advertise a CRL distribution point."));
        }

        return Task.FromResult(new RevocationCheckResult(
            RevocationStatus.Unknown,
            SourceName,
            "CRL distribution point present but live CRL fetch is not enabled in this MVP build."));
    }
}
