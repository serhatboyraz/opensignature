using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Crypto;
using OpenSignature.Validation.Revocation;

namespace OpenSignature.Validation.Certificates;

/// <summary>
/// Certificate validation pipeline:
/// Parse → validity → chain → trust anchor → key usage → EKU → revocation → policy → result.
/// Uses .NET <see cref="X509Chain"/> for basic path building. Private keys are never used.
/// </summary>
/// <remarks>
/// Limitations (MVP): not a full ETSI EN 319 102-1 / RFC 5280 policy-mapping implementation.
/// Online OCSP/CRL fetch is designed but not live-network enabled by default.
/// </remarks>
public sealed class CertificateValidator : ICertificateValidator
{
    private readonly IRevocationChecker _revocationChecker;

    public CertificateValidator(IRevocationChecker? revocationChecker = null)
    {
        _revocationChecker = revocationChecker ?? OfflineRevocationChecker.Instance;
    }

    public async Task<CertificateValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> publicCertificateDer,
        CertificateValidationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new CertificateValidationOptions();

        X509Certificate2 certificate;
        try
        {
            certificate = CertificateHelper.LoadPublic(publicCertificateDer.Span);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertParseFailed,
                detail: "Unable to parse public certificate DER.");
        }

        using (certificate)
        {
            return await ValidateAsync(certificate, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CertificateValidationResult> ValidateAsync(
        X509Certificate2 certificate,
        CertificateValidationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new CertificateValidationOptions();

        var asOf = options.AsOf ?? DateTimeOffset.UtcNow;
        var checkedAt = DateTimeOffset.UtcNow;
        var subject = certificate.Subject;
        var issuer = certificate.Issuer;
        var thumbprint = certificate.Thumbprint;
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());

        // 1) Time validity
        if (asOf < notBefore)
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertNotYetValid,
                detail: $"Certificate is not valid before {notBefore:O}.",
                subject, issuer, thumbprint, notBefore, notAfter,
                checkedAt: checkedAt);
        }

        if (asOf > notAfter)
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertExpired,
                detail: $"Certificate expired at {notAfter:O}.",
                subject, issuer, thumbprint, notBefore, notAfter,
                checkedAt: checkedAt);
        }

        // 2–3) Chain building + trust anchor
        var (chainOk, chainStatuses, issuerCert) = BuildChain(certificate, options, asOf);
        if (!chainOk)
        {
            var untrusted = chainStatuses.Any(static s =>
                s.Contains("UntrustedRoot", StringComparison.OrdinalIgnoreCase)
                || s.Contains("PartialChain", StringComparison.OrdinalIgnoreCase));

            return CertificateValidationResult.Failure(
                untrusted
                    ? CertificateValidationCodes.CertUntrusted
                    : CertificateValidationCodes.CertChainInvalid,
                detail: string.Join("; ", chainStatuses),
                subject, issuer, thumbprint, notBefore, notAfter,
                chainStatus: chainStatuses,
                checkedAt: checkedAt);
        }

        // 4) Key usage
        if (options.RequireSigningKeyUsage && !HasSigningKeyUsage(certificate))
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertKeyUsageInvalid,
                detail: "Certificate key usage does not allow digitalSignature or nonRepudiation.",
                subject, issuer, thumbprint, notBefore, notAfter,
                chainStatus: chainStatuses,
                checkedAt: checkedAt);
        }

        // 5) EKU
        if (!HasAllowedEku(certificate, options.AllowedEnhancedKeyUsageOids))
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertEkuInvalid,
                detail: "Certificate enhanced key usage does not match the validation policy.",
                subject, issuer, thumbprint, notBefore, notAfter,
                chainStatus: chainStatuses,
                checkedAt: checkedAt);
        }

        // 6) Revocation (OCSP + CRL via injected checker)
        var revocationChecker = ResolveRevocationChecker(options);
        var revocation = await revocationChecker
            .CheckAsync(certificate, issuerCert, asOf, cancellationToken)
            .ConfigureAwait(false);

        if (revocation.Status == RevocationStatus.Revoked)
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertRevoked,
                detail: revocation.Detail,
                subject, issuer, thumbprint, notBefore, notAfter,
                chainStatus: chainStatuses,
                revocation: revocation,
                checkedAt: checkedAt);
        }

        if (revocation.Status == RevocationStatus.Unknown
            && options.RevocationMode == RevocationMode.Online)
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertRevocationUnknown,
                detail: revocation.Detail,
                subject, issuer, thumbprint, notBefore, notAfter,
                chainStatus: chainStatuses,
                revocation: revocation,
                checkedAt: checkedAt);
        }

        // 7) Policy
        if (!SatisfiesPolicies(certificate, options.RequiredCertificatePolicyOids))
        {
            return CertificateValidationResult.Failure(
                CertificateValidationCodes.CertPolicyInvalid,
                detail: "Certificate policies do not satisfy the required policy OIDs.",
                subject, issuer, thumbprint, notBefore, notAfter,
                chainStatus: chainStatuses,
                revocation: revocation,
                checkedAt: checkedAt);
        }

        // 8) Result
        return CertificateValidationResult.Success(
            subject, issuer, thumbprint, notBefore, notAfter, chainStatuses, revocation, checkedAt);
    }

    private IRevocationChecker ResolveRevocationChecker(CertificateValidationOptions options)
    {
        // If the injected checker is already a composite or custom stub, prefer it for Offline.
        // For Online/SoftFail, wrap with Composite so OCSP→CRL policy is applied unless caller
        // already supplied a CompositeRevocationChecker.
        if (_revocationChecker is CompositeRevocationChecker)
        {
            return _revocationChecker;
        }

        if (options.RevocationMode == RevocationMode.Offline)
        {
            // Prefer caller-provided stub (tests) over forcing Offline skip.
            if (!ReferenceEquals(_revocationChecker, OfflineRevocationChecker.Instance))
            {
                return _revocationChecker;
            }

            return OfflineRevocationChecker.Instance;
        }

        return new CompositeRevocationChecker(options.RevocationMode, offline: _revocationChecker);
    }

    private static (bool Ok, IReadOnlyList<string> Statuses, X509Certificate2? Issuer) BuildChain(
        X509Certificate2 certificate,
        CertificateValidationOptions options,
        DateTimeOffset asOf)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationTime = asOf.UtcDateTime;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        foreach (var extra in options.ExtraCertificates)
        {
            chain.ChainPolicy.ExtraStore.Add(extra);
        }

        if (options.UseCustomTrustStore)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (var anchor in options.TrustAnchors)
            {
                chain.ChainPolicy.CustomTrustStore.Add(anchor);
            }

            // Self-signed leaf used as its own trust anchor (common in tests/MVP).
            if (options.AllowSelfSignedWhenTrusted
                && options.TrustAnchors.Count == 0
                && IsSelfSigned(certificate))
            {
                chain.ChainPolicy.CustomTrustStore.Add(certificate);
            }
        }

        var built = chain.Build(certificate);
        var statuses = chain.ChainStatus
            .Select(static s => $"{s.Status}: {s.StatusInformation}".Trim())
            .ToArray();

        X509Certificate2? issuer = null;
        if (chain.ChainElements.Count > 1)
        {
            issuer = chain.ChainElements[1].Certificate;
        }

        return (built, statuses, issuer);
    }

    private static bool IsSelfSigned(X509Certificate2 certificate) =>
        string.Equals(certificate.Subject, certificate.Issuer, StringComparison.Ordinal);

    private static bool HasSigningKeyUsage(X509Certificate2 certificate)
    {
        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is null)
        {
            // No KU extension → unrestricted for MVP.
            return true;
        }

        const X509KeyUsageFlags signing =
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;

        return (keyUsage.KeyUsages & signing) != X509KeyUsageFlags.None;
    }

    private static bool HasAllowedEku(X509Certificate2 certificate, IList<string> allowedOids)
    {
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (eku is null || eku.EnhancedKeyUsages.Count == 0)
        {
            return true;
        }

        if (allowedOids.Count == 0)
        {
            return true;
        }

        foreach (Oid oid in eku.EnhancedKeyUsages)
        {
            if (oid.Value is not null
                && allowedOids.Any(a => string.Equals(a, oid.Value, StringComparison.Ordinal)))
            {
                return true;
            }

            // anyExtendedKeyUsage
            if (string.Equals(oid.Value, "2.5.29.37.0", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SatisfiesPolicies(X509Certificate2 certificate, IList<string> requiredPolicies)
    {
        if (requiredPolicies.Count == 0)
        {
            return true;
        }

        var policies = certificate.Extensions
            .OfType<X509Extension>()
            .Where(static e => e.Oid?.Value == "2.5.29.32")
            .SelectMany(static e => ParsePolicyOids(e))
            .ToHashSet(StringComparer.Ordinal);

        return requiredPolicies.All(policies.Contains);
    }

    private static IEnumerable<string> ParsePolicyOids(X509Extension extension)
    {
        // Best-effort: Format(false) often contains OID tokens; avoid claiming ASN.1 completeness.
        var formatted = extension.Format(false);
        foreach (var token in formatted.Split([' ', ',', '\r', '\n', '='], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.All(static c => char.IsDigit(c) || c == '.') && token.Contains('.', StringComparison.Ordinal))
            {
                yield return token;
            }
        }
    }
}
