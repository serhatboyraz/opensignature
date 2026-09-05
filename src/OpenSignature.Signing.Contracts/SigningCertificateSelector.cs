using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Criteria used to resolve a signing certificate inside a provider.
/// At least one criterion must be supplied. Private keys are never selected or returned.
/// </summary>
public sealed class SigningCertificateSelector
{
    private SigningCertificateSelector(
        CertificateThumbprint? thumbprint,
        string? serialNumber,
        string? issuer,
        string? providerReference,
        string? subjectContains)
    {
        if (thumbprint is null
            && string.IsNullOrWhiteSpace(serialNumber)
            && string.IsNullOrWhiteSpace(providerReference)
            && string.IsNullOrWhiteSpace(subjectContains))
        {
            throw new ArgumentException(
                "At least one certificate selection criterion must be provided.",
                nameof(thumbprint));
        }

        if (!string.IsNullOrWhiteSpace(serialNumber) && string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException(
                "Issuer is required when selecting a certificate by serial number.",
                nameof(issuer));
        }

        Thumbprint = thumbprint;
        SerialNumber = NormalizeOptional(serialNumber);
        Issuer = NormalizeOptional(issuer);
        ProviderReference = NormalizeOptional(providerReference);
        SubjectContains = NormalizeOptional(subjectContains);
    }

    /// <summary>SHA-1 or SHA-256 certificate thumbprint when selecting by thumbprint.</summary>
    public CertificateThumbprint? Thumbprint { get; }

    /// <summary>Certificate serial number; requires <see cref="Issuer"/>.</summary>
    public string? SerialNumber { get; }

    /// <summary>Certificate issuer DN; used with <see cref="SerialNumber"/>.</summary>
    public string? Issuer { get; }

    /// <summary>Provider-local reference (slot, label, PFX path key, etc.).</summary>
    public string? ProviderReference { get; }

    /// <summary>Optional subject substring match for discovery helpers.</summary>
    public string? SubjectContains { get; }

    public static SigningCertificateSelector ByThumbprint(CertificateThumbprint thumbprint)
    {
        ArgumentNullException.ThrowIfNull(thumbprint);
        return new SigningCertificateSelector(thumbprint, null, null, null, null);
    }

    public static SigningCertificateSelector ByThumbprint(string thumbprint) =>
        ByThumbprint(CertificateThumbprint.Create(thumbprint));

    public static SigningCertificateSelector ByProviderReference(string providerReference)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("Provider reference must not be empty.", nameof(providerReference));
        }

        return new SigningCertificateSelector(null, null, null, providerReference, null);
    }

    public static SigningCertificateSelector BySerialNumberAndIssuer(string serialNumber, string issuer)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Serial number must not be empty.", nameof(serialNumber));
        }

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("Issuer must not be empty.", nameof(issuer));
        }

        return new SigningCertificateSelector(null, serialNumber, issuer, null, null);
    }

    public static SigningCertificateSelector BySubjectContains(string subjectContains)
    {
        if (string.IsNullOrWhiteSpace(subjectContains))
        {
            throw new ArgumentException("Subject filter must not be empty.", nameof(subjectContains));
        }

        return new SigningCertificateSelector(null, null, null, null, subjectContains);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
