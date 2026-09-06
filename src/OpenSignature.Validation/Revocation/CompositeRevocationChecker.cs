using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Revocation;

/// <summary>
/// Composite checker: respects <see cref="RevocationMode"/>, tries OCSP then CRL for Online/SoftFail.
/// </summary>
public sealed class CompositeRevocationChecker : IRevocationChecker
{
    private readonly RevocationMode _mode;
    private readonly IRevocationChecker _ocsp;
    private readonly IRevocationChecker _crl;
    private readonly IRevocationChecker _offline;

    public CompositeRevocationChecker(
        RevocationMode mode,
        IRevocationChecker? ocsp = null,
        IRevocationChecker? crl = null,
        IRevocationChecker? offline = null)
    {
        _mode = mode;
        _ocsp = ocsp ?? new OcspRevocationChecker();
        _crl = crl ?? new CrlRevocationChecker();
        _offline = offline ?? OfflineRevocationChecker.Instance;
    }

    public async Task<RevocationCheckResult> CheckAsync(
        X509Certificate2 certificate,
        X509Certificate2? issuer,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (_mode == RevocationMode.Offline)
        {
            return await _offline.CheckAsync(certificate, issuer, asOf, cancellationToken).ConfigureAwait(false);
        }

        var ocsp = await _ocsp.CheckAsync(certificate, issuer, asOf, cancellationToken).ConfigureAwait(false);
        if (ocsp.Status is RevocationStatus.Good or RevocationStatus.Revoked)
        {
            return ocsp;
        }

        var crl = await _crl.CheckAsync(certificate, issuer, asOf, cancellationToken).ConfigureAwait(false);
        if (crl.Status is RevocationStatus.Good or RevocationStatus.Revoked)
        {
            return crl;
        }

        // Both unknown
        if (_mode == RevocationMode.SoftFail)
        {
            return new RevocationCheckResult(
                RevocationStatus.Unknown,
                "OCSP+CRL",
                "Revocation status unknown; SoftFail mode treats this as non-fatal.");
        }

        return new RevocationCheckResult(
            RevocationStatus.Unknown,
            "OCSP+CRL",
            "Revocation status could not be determined via OCSP or CRL.");
    }
}
