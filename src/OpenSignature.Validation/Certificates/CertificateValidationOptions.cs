using System.Security.Cryptography.X509Certificates;
using OpenSignature.Validation.Revocation;

namespace OpenSignature.Validation.Certificates;

/// <summary>
/// Options for <see cref="ICertificateValidator"/>.
/// Trust anchors are public certificates only; private keys must never be supplied.
/// </summary>
public sealed class CertificateValidationOptions
{
    /// <summary>When true, build chains against <see cref="TrustAnchors"/> instead of the system store.</summary>
    public bool UseCustomTrustStore { get; set; }

    /// <summary>Custom trust anchors (public DER or loaded public certificates).</summary>
    public IList<X509Certificate2> TrustAnchors { get; } = new List<X509Certificate2>();

    /// <summary>Additional certificates available for chain building (intermediates).</summary>
    public IList<X509Certificate2> ExtraCertificates { get; } = new List<X509Certificate2>();

    /// <summary>Instant used for validity and chain VerificationTime. Defaults to UtcNow.</summary>
    public DateTimeOffset? AsOf { get; set; }

    /// <summary>Revocation evaluation mode.</summary>
    public RevocationMode RevocationMode { get; set; } = RevocationMode.Offline;

    /// <summary>When true, require DigitalSignature and/or NonRepudiation key usage.</summary>
    public bool RequireSigningKeyUsage { get; set; } = true;

    /// <summary>
    /// Allowed EKU OIDs when an EKU extension is present.
    /// Empty list means any EKU (or absence of EKU) is accepted.
    /// </summary>
    public IList<string> AllowedEnhancedKeyUsageOids { get; } = new List<string>();

    /// <summary>
    /// Required certificate policy OIDs. Empty means no policy constraint.
    /// </summary>
    public IList<string> RequiredCertificatePolicyOids { get; } = new List<string>();

    /// <summary>
    /// When true (default), self-signed certificates may be accepted if they are also trust anchors
    /// under a custom trust store, or when chain building succeeds.
    /// </summary>
    public bool AllowSelfSignedWhenTrusted { get; set; } = true;
}
