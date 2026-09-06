using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Revocation;

/// <summary>
/// Stub revocation checker used for Offline mode and unit tests.
/// Does not perform network I/O; returns <see cref="RevocationStatus.Skipped"/>.
/// </summary>
public sealed class OfflineRevocationChecker : IRevocationChecker
{
    public static OfflineRevocationChecker Instance { get; } = new();

    public Task<RevocationCheckResult> CheckAsync(
        X509Certificate2 certificate,
        X509Certificate2? issuer,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new RevocationCheckResult(
            RevocationStatus.Skipped,
            "Offline",
            "Revocation checking skipped (Offline mode)."));
    }
}
