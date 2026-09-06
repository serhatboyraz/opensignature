using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Orchestration;

namespace OpenSignature.Signing.Timestamping;

/// <summary>
/// Default TSA when none is configured. T/LT/LTA requests fail instead of downgrading to Baseline B.
/// </summary>
public sealed class UnavailableTimestampAuthority : ITimestampAuthority
{
    public static UnavailableTimestampAuthority Instance { get; } = new();

    public Task<TimestampToken> GetTimestampAsync(
        byte[] messageImprint,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        throw new TimestampAuthorityUnavailableException(
            "No RFC 3161 timestamp authority is configured. T/LT/LTA profiles cannot be produced and are never silently downgraded to Baseline B.");
    }
}
