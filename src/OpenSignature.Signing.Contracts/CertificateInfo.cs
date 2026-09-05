using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Public certificate metadata discovered from a signing provider.
/// Must never carry private key material.
/// </summary>
public sealed class CertificateInfo
{
    public CertificateInfo(
        CertificateThumbprint thumbprint,
        string subject,
        string issuer,
        string serialNumber,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        string providerReference,
        bool canSign,
        byte[]? publicCertificateDer = null,
        string? friendlyName = null,
        string? publicKeyAlgorithm = null,
        int? keySizeBits = null,
        IReadOnlyList<string>? keyUsages = null,
        IReadOnlyList<string>? enhancedKeyUsages = null)
    {
        ArgumentNullException.ThrowIfNull(thumbprint);

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Certificate subject must not be empty.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("Certificate issuer must not be empty.", nameof(issuer));
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Certificate serial number must not be empty.", nameof(serialNumber));
        }

        if (notAfter < notBefore)
        {
            throw new ArgumentException("Certificate NotAfter must be on or after NotBefore.", nameof(notAfter));
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("Provider reference must not be empty.", nameof(providerReference));
        }

        if (keySizeBits is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keySizeBits), keySizeBits, "Key size must be positive when specified.");
        }

        Thumbprint = thumbprint;
        Subject = subject.Trim();
        Issuer = issuer.Trim();
        SerialNumber = serialNumber.Trim();
        NotBefore = notBefore;
        NotAfter = notAfter;
        ProviderReference = providerReference.Trim();
        CanSign = canSign;
        PublicCertificateDer = publicCertificateDer is null
            ? Array.Empty<byte>()
            : publicCertificateDer.ToArray();
        FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim();
        PublicKeyAlgorithm = string.IsNullOrWhiteSpace(publicKeyAlgorithm) ? null : publicKeyAlgorithm.Trim();
        KeySizeBits = keySizeBits;
        KeyUsages = keyUsages is null || keyUsages.Count == 0
            ? Array.Empty<string>()
            : keyUsages.ToArray();
        EnhancedKeyUsages = enhancedKeyUsages is null || enhancedKeyUsages.Count == 0
            ? Array.Empty<string>()
            : enhancedKeyUsages.ToArray();
    }

    public CertificateThumbprint Thumbprint { get; }

    public string Subject { get; }

    public string Issuer { get; }

    public string SerialNumber { get; }

    public DateTimeOffset NotBefore { get; }

    public DateTimeOffset NotAfter { get; }

    /// <summary>Provider-local reference used to re-resolve the certificate for signing.</summary>
    public string ProviderReference { get; }

    /// <summary>
    /// True when the provider can produce signatures with this certificate.
    /// Does not imply that private key material is available to the caller.
    /// </summary>
    public bool CanSign { get; }

    /// <summary>DER-encoded X.509 public certificate bytes. Never includes a private key.</summary>
    public IReadOnlyList<byte> PublicCertificateDer { get; }

    public string? FriendlyName { get; }

    public string? PublicKeyAlgorithm { get; }

    public int? KeySizeBits { get; }

    public IReadOnlyList<string> KeyUsages { get; }

    public IReadOnlyList<string> EnhancedKeyUsages { get; }

    public bool IsCurrentlyValid(DateTimeOffset? asOf = null)
    {
        var instant = asOf ?? DateTimeOffset.UtcNow;
        return instant >= NotBefore && instant <= NotAfter;
    }
}
