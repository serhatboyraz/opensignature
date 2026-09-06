using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Maps PKCS#11 public certificate objects to <see cref="CertificateInfo"/>.
/// Never handles private key material.
/// </summary>
internal static class Pkcs11CertificateMapper
{
    public static CertificateInfo ToCertificateInfo(
        Pkcs11CertificateObject certificateObject,
        ulong slotId,
        string providerScheme,
        int index,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(certificateObject);

        using var certificate = X509CertificateLoader.LoadCertificate(certificateObject.CertificateDer);
        var thumbprint = CertificateThumbprint.Create(certificate.Thumbprint);
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
        var currentlyValid = asOf >= notBefore && asOf <= notAfter;

        string? publicKeyAlgorithm = null;
        int? keySizeBits = null;
        using (var rsa = certificate.GetRSAPublicKey())
        using (var ecdsa = certificate.GetECDsaPublicKey())
        {
            if (rsa is not null)
            {
                publicKeyAlgorithm = "RSA";
                keySizeBits = rsa.KeySize;
            }
            else if (ecdsa is not null)
            {
                publicKeyAlgorithm = "ECDSA";
                keySizeBits = ecdsa.KeySize;
            }
        }

        var canSign = certificateObject.HasMatchingPrivateKey
            && currentlyValid
            && publicKeyAlgorithm is not null;

        var labelPart = certificateObject.Label ?? thumbprint.Value;
        var providerReference = $"{providerScheme}://{slotId}/{labelPart}";

        return new CertificateInfo(
            thumbprint: thumbprint,
            subject: certificate.Subject,
            issuer: certificate.Issuer,
            serialNumber: certificate.SerialNumber,
            notBefore: notBefore,
            notAfter: notAfter,
            providerReference: providerReference,
            canSign: canSign,
            publicCertificateDer: certificateObject.CertificateDer.ToArray(),
            friendlyName: certificateObject.Label,
            publicKeyAlgorithm: publicKeyAlgorithm,
            keySizeBits: keySizeBits,
            keyUsages: ReadKeyUsages(certificate),
            enhancedKeyUsages: ReadEnhancedKeyUsages(certificate));
    }

    public static bool Matches(CertificateInfo certificate, SigningCertificateSelector selector)
    {
        if (selector.Thumbprint is not null
            && !string.Equals(certificate.Thumbprint.Value, selector.Thumbprint.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.ProviderReference is not null
            && !string.Equals(certificate.ProviderReference, selector.ProviderReference, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.SerialNumber is not null)
        {
            if (!string.Equals(certificate.SerialNumber, selector.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (selector.Issuer is not null
                && !string.Equals(certificate.Issuer, selector.Issuer, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (selector.SubjectContains is not null
            && !certificate.Subject.Contains(selector.SubjectContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return selector.Thumbprint is not null
            || selector.ProviderReference is not null
            || selector.SerialNumber is not null
            || selector.SubjectContains is not null;
    }

    public static Pkcs11ObjectFilter? CreateConfiguredFilter(Pkcs11ProviderOptionsBase options)
    {
        ArgumentNullException.ThrowIfNull(options);

        byte[]? id = null;
        if (!string.IsNullOrWhiteSpace(options.CertificateIdHex))
        {
            id = Convert.FromHexString(options.CertificateIdHex.Trim());
        }

        if (string.IsNullOrWhiteSpace(options.CertificateLabel) && id is null)
        {
            return null;
        }

        return new Pkcs11ObjectFilter
        {
            Label = string.IsNullOrWhiteSpace(options.CertificateLabel) ? null : options.CertificateLabel.Trim(),
            Id = id
        };
    }

    public static Pkcs11ObjectFilter CreateKeyFilter(Pkcs11CertificateObject certificateObject)
    {
        ArgumentNullException.ThrowIfNull(certificateObject);

        if (certificateObject.Id.Length > 0)
        {
            return new Pkcs11ObjectFilter { Id = certificateObject.Id.ToArray() };
        }

        if (!string.IsNullOrWhiteSpace(certificateObject.Label))
        {
            return new Pkcs11ObjectFilter { Label = certificateObject.Label };
        }

        throw new InvalidOperationException(
            "Certificate object has neither CKA_ID nor CKA_LABEL to locate a matching private key.");
    }

    private static IReadOnlyList<string> ReadKeyUsages(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (extension is null)
        {
            return Array.Empty<string>();
        }

        var usages = new List<string>();
        var flags = extension.KeyUsages;
        if (flags.HasFlag(X509KeyUsageFlags.DigitalSignature))
        {
            usages.Add(nameof(X509KeyUsageFlags.DigitalSignature));
        }

        if (flags.HasFlag(X509KeyUsageFlags.NonRepudiation))
        {
            usages.Add(nameof(X509KeyUsageFlags.NonRepudiation));
        }

        return usages.Count == 0 ? Array.Empty<string>() : usages;
    }

    private static IReadOnlyList<string> ReadEnhancedKeyUsages(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (extension is null || extension.EnhancedKeyUsages.Count == 0)
        {
            return Array.Empty<string>();
        }

        return extension.EnhancedKeyUsages
            .Cast<Oid>()
            .Select(static oid => oid.Value ?? oid.FriendlyName ?? string.Empty)
            .Where(static value => value.Length > 0)
            .ToArray();
    }
}
