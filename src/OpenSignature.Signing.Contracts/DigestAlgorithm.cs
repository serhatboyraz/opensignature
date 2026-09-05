namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Digest algorithms accepted by <see cref="ISigningProvider.SignDigestAsync"/>.
/// Digest computation happens outside the provider; the provider only signs the digest bytes.
/// </summary>
public enum DigestAlgorithm
{
    Sha256 = 0,
    Sha384 = 1,
    Sha512 = 2
}

/// <summary>
/// Helpers for <see cref="DigestAlgorithm"/>.
/// </summary>
public static class DigestAlgorithmExtensions
{
    /// <summary>
    /// Returns the expected digest length in bytes for the algorithm.
    /// </summary>
    public static int GetDigestLengthBytes(this DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => 32,
            DigestAlgorithm.Sha384 => 48,
            DigestAlgorithm.Sha512 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };
}
