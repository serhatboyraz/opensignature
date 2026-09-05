using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Signing.Crypto;

/// <summary>
/// Certificate helpers for public DER export and validity checks.
/// Never handles private keys.
/// </summary>
public static class CertificateHelper
{
    /// <summary>Exports the public X.509 certificate as DER (no private key).</summary>
    public static byte[] ExportPublicDer(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.Export(X509ContentType.Cert);
    }

    /// <summary>Loads a public certificate from DER bytes.</summary>
    public static X509Certificate2 LoadPublic(ReadOnlySpan<byte> der) =>
        X509CertificateLoader.LoadCertificate(der);

    /// <summary>Returns whether <paramref name="certificate"/> is within its validity window.</summary>
    public static bool IsCurrentlyValid(X509Certificate2 certificate, DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var instant = asOf ?? DateTimeOffset.UtcNow;
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
        return instant >= notBefore && instant <= notAfter;
    }

    /// <summary>
    /// Builds a DER-encoded ESS SigningCertificateV2 attribute value
    /// (OID 1.2.840.113549.1.9.16.2.47) for CAdES Baseline B.
    /// </summary>
    public static byte[] BuildSigningCertificateV2AttributeValue(
        X509Certificate2 certificate,
        DigestAlgorithmHash hashAlgorithm = DigestAlgorithmHash.Sha256)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var certDer = ExportPublicDer(certificate);
        var certHash = hashAlgorithm switch
        {
            DigestAlgorithmHash.Sha256 => SHA256.HashData(certDer),
            DigestAlgorithmHash.Sha384 => SHA384.HashData(certDer),
            DigestAlgorithmHash.Sha512 => SHA512.HashData(certDer),
            _ => throw new ArgumentOutOfRangeException(nameof(hashAlgorithm))
        };

        var hashOid = hashAlgorithm switch
        {
            DigestAlgorithmHash.Sha256 => "2.16.840.1.101.3.4.2.1",
            DigestAlgorithmHash.Sha384 => "2.16.840.1.101.3.4.2.2",
            DigestAlgorithmHash.Sha512 => "2.16.840.1.101.3.4.2.3",
            _ => throw new ArgumentOutOfRangeException(nameof(hashAlgorithm))
        };

        // SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2, policies OPTIONAL }
        // ESSCertIDv2 ::= SEQUENCE { hashAlgorithm AlgorithmIdentifier DEFAULT sha256, certHash Hash, issuerSerial OPTIONAL }
        var writer = new AsnWriter(AsnEncodingRules.DER);

        writer.PushSequence(); // SigningCertificateV2
        writer.PushSequence(); // certs
        writer.PushSequence(); // ESSCertIDv2

        writer.PushSequence(); // AlgorithmIdentifier
        writer.WriteObjectIdentifier(hashOid);
        writer.PopSequence();

        writer.WriteOctetString(certHash);

        // IssuerSerial OPTIONAL — include for better interoperability
        writer.PushSequence(); // IssuerSerial
        writer.PushSequence(); // GeneralNames
        writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
        writer.WriteEncodedValue(certificate.IssuerName.RawData);
        writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
        writer.PopSequence(); // GeneralNames
        writer.WriteInteger(HexToBigIntegerBytes(certificate.SerialNumber));
        writer.PopSequence(); // IssuerSerial

        writer.PopSequence(); // ESSCertIDv2
        writer.PopSequence(); // certs
        writer.PopSequence(); // SigningCertificateV2

        return writer.Encode();
    }

    /// <summary>OID for id-aa-signingCertificateV2.</summary>
    public const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    private static byte[] HexToBigIntegerBytes(string serialHex)
    {
        var cleaned = serialHex.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length % 2 != 0)
        {
            cleaned = "0" + cleaned;
        }

        var bytes = Convert.FromHexString(cleaned);
        // ASN.1 INTEGER must be two's complement; prepend 0x00 when high bit set.
        if (bytes.Length > 0 && (bytes[0] & 0x80) != 0)
        {
            var prefixed = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, prefixed, 1, bytes.Length);
            return prefixed;
        }

        return bytes;
    }
}

/// <summary>Hash algorithms used when building certificate digests for ESS attributes.</summary>
public enum DigestAlgorithmHash
{
    Sha256 = 0,
    Sha384 = 1,
    Sha512 = 2
}
