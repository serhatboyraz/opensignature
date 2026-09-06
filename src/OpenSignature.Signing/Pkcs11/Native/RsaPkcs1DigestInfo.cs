using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Pkcs11.Native;

/// <summary>
/// DER DigestInfo prefixes for CKM_RSA_PKCS signing of a pre-computed digest (RFC 8017).
/// </summary>
internal static class RsaPkcs1DigestInfo
{
    // SHA-256: SEQUENCE { SEQUENCE { OID 2.16.840.1.101.3.4.2.1, NULL }, OCTET STRING (32) }
    private static readonly byte[] Sha256Prefix =
    [
        0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00,
        0x04, 0x20
    ];

    private static readonly byte[] Sha384Prefix =
    [
        0x30, 0x41, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x02, 0x05, 0x00,
        0x04, 0x30
    ];

    private static readonly byte[] Sha512Prefix =
    [
        0x30, 0x51, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x03, 0x05, 0x00,
        0x04, 0x40
    ];

    public static byte[] Wrap(ReadOnlySpan<byte> digest, DigestAlgorithm algorithm)
    {
        var prefix = algorithm switch
        {
            DigestAlgorithm.Sha256 => Sha256Prefix,
            DigestAlgorithm.Sha384 => Sha384Prefix,
            DigestAlgorithm.Sha512 => Sha512Prefix,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };

        var expected = algorithm.GetDigestLengthBytes();
        if (digest.Length != expected)
        {
            throw new ArgumentException(
                $"Digest length {digest.Length} does not match {algorithm} expected length {expected}.",
                nameof(digest));
        }

        var result = new byte[prefix.Length + digest.Length];
        prefix.CopyTo(result);
        digest.CopyTo(result.AsSpan(prefix.Length));
        return result;
    }
}
