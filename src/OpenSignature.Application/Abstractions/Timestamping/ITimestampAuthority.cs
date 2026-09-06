using OpenSignature.Signing.Contracts;

namespace OpenSignature.Application.Abstractions.Timestamping;

/// <summary>
/// RFC 3161 timestamp authority. Implementations must never silently skip timestamping.
/// </summary>
public interface ITimestampAuthority
{
    /// <summary>
    /// Requests a timestamp token over <paramref name="messageImprint"/> (already hashed).
    /// </summary>
    Task<TimestampToken> GetTimestampAsync(
        byte[] messageImprint,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default);
}

/// <summary>RFC 3161 TimeStampToken (CMS SignedData containing TSTInfo).</summary>
/// <param name="Encoded">DER-encoded TimeStampToken (ContentInfo).</param>
/// <param name="GenerationTime">TSTInfo genTime (UTC).</param>
public sealed record TimestampToken(byte[] Encoded, DateTimeOffset GenerationTime);
