using System.Security.Cryptography;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Crypto;

/// <summary>
/// Digest helpers wrapping <see cref="DigestAlgorithm"/> with BCL hash algorithms.
/// </summary>
public static class DigestHelper
{
    /// <summary>Computes a digest over <paramref name="data"/>.</summary>
    public static byte[] ComputeDigest(ReadOnlySpan<byte> data, DigestAlgorithm algorithm)
    {
        return algorithm switch
        {
            DigestAlgorithm.Sha256 => SHA256.HashData(data),
            DigestAlgorithm.Sha384 => SHA384.HashData(data),
            DigestAlgorithm.Sha512 => SHA512.HashData(data),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };
    }

    /// <summary>Computes a digest by reading <paramref name="stream"/> from its current position.</summary>
    public static async Task<byte[]> ComputeDigestAsync(
        Stream stream,
        DigestAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var hash = CreateHashAlgorithm(algorithm);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.TransformBlock(buffer, 0, read, null, 0);
        }

        hash.TransformFinalBlock([], 0, 0);
        return hash.Hash ?? throw new CryptographicException("Hash algorithm produced no digest.");
    }

    /// <summary>Maps <see cref="DigestAlgorithm"/> to <see cref="HashAlgorithmName"/>.</summary>
    public static HashAlgorithmName ToHashAlgorithmName(DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => HashAlgorithmName.SHA256,
            DigestAlgorithm.Sha384 => HashAlgorithmName.SHA384,
            DigestAlgorithm.Sha512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };

    /// <summary>Maps <see cref="HashAlgorithmName"/> to <see cref="DigestAlgorithm"/>.</summary>
    public static DigestAlgorithm FromHashAlgorithmName(HashAlgorithmName hashAlgorithm)
    {
        if (hashAlgorithm == HashAlgorithmName.SHA256)
        {
            return DigestAlgorithm.Sha256;
        }

        if (hashAlgorithm == HashAlgorithmName.SHA384)
        {
            return DigestAlgorithm.Sha384;
        }

        if (hashAlgorithm == HashAlgorithmName.SHA512)
        {
            return DigestAlgorithm.Sha512;
        }

        throw new ArgumentOutOfRangeException(
            nameof(hashAlgorithm),
            hashAlgorithm.Name,
            "Unsupported hash algorithm name.");
    }

    /// <summary>Returns the digest OID used by CMS/XMLDSig (e.g. 2.16.840.1.101.3.4.2.1 for SHA-256).</summary>
    public static string GetOid(DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => "2.16.840.1.101.3.4.2.1",
            DigestAlgorithm.Sha384 => "2.16.840.1.101.3.4.2.2",
            DigestAlgorithm.Sha512 => "2.16.840.1.101.3.4.2.3",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };

    /// <summary>Returns the XMLDSig digest algorithm URI.</summary>
    public static string GetXmlDsigUri(DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => "http://www.w3.org/2001/04/xmlenc#sha256",
            DigestAlgorithm.Sha384 => "http://www.w3.org/2001/04/xmldsig-more#sha384",
            DigestAlgorithm.Sha512 => "http://www.w3.org/2001/04/xmlenc#sha512",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };

    /// <summary>Returns the XMLDSig signature method URI for RSA-SHA* algorithms.</summary>
    public static string GetRsaShaSignatureMethodUri(DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => SignedXmlSignatureMethods.RsaSha256,
            DigestAlgorithm.Sha384 => SignedXmlSignatureMethods.RsaSha384,
            DigestAlgorithm.Sha512 => SignedXmlSignatureMethods.RsaSha512,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };

    private static HashAlgorithm CreateHashAlgorithm(DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => SHA256.Create(),
            DigestAlgorithm.Sha384 => SHA384.Create(),
            DigestAlgorithm.Sha512 => SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };
}

/// <summary>XMLDSig signature method URIs used by OpenSignature.</summary>
public static class SignedXmlSignatureMethods
{
    public const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
    public const string RsaSha384 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384";
    public const string RsaSha512 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512";
}
