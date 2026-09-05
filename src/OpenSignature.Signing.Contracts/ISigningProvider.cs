using OpenSignature.Domain.Enums;

namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Abstraction over a signing backend (PFX, PKCS#11, smart card, HSM).
/// Implementations must keep private keys inside the provider or hardware device and
/// prefer a digest-to-device-sign flow. Private keys must never be exposed or exported.
/// </summary>
public interface ISigningProvider : IAsyncDisposable
{
    /// <summary>Stable identifier for this provider instance (configuration key).</summary>
    string ProviderId { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>Provider category from the domain model.</summary>
    SigningProviderType ProviderType { get; }

    /// <summary>Lists certificates visible through this provider (public metadata only).</summary>
    Task<IReadOnlyList<CertificateInfo>> ListCertificatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single certificate matching <paramref name="selector"/>.
    /// Returns <c>null</c> when no match is found.
    /// </summary>
    Task<CertificateInfo?> GetCertificateAsync(
        SigningCertificateSelector selector,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs a pre-computed digest with the private key bound to the selected certificate.
    /// Callers must never receive private key material; only the signature bytes are returned.
    /// </summary>
    /// <param name="digest">Digest bytes (length must match <paramref name="digestAlgorithm"/>).</param>
    /// <param name="digestAlgorithm">Algorithm that produced <paramref name="digest"/>.</param>
    /// <param name="certificateSelector">Selector resolving the signing certificate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw signature bytes produced by the provider/device.</returns>
    Task<byte[]> SignDigestAsync(
        ReadOnlyMemory<byte> digest,
        DigestAlgorithm digestAlgorithm,
        SigningCertificateSelector certificateSelector,
        CancellationToken cancellationToken = default);

    /// <summary>Reports provider availability and health.</summary>
    Task<ProviderHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default);
}
