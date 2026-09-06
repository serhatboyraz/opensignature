namespace OpenSignature.Signing;

/// <summary>
/// Cross-provider signing policy bound from the <c>Signing</c> configuration section.
/// </summary>
public sealed class SigningOptions
{
    public const string SectionName = "Signing";

    /// <summary>
    /// When true, providers may use certificates outside <c>NotBefore</c>/<c>NotAfter</c>
    /// if a private key is available. Default is false.
    /// Verification still reports expiration. Intended for development (for example an expired USB-token certificate).
    /// </summary>
    public bool AllowExpiredCertificates { get; set; }

    /// <summary>
    /// Returns whether the certificate validity window allows signing at <paramref name="asOf"/>.
    /// When <see cref="AllowExpiredCertificates"/> is true, always returns true.
    /// </summary>
    public bool AllowsSigningAt(DateTimeOffset notBefore, DateTimeOffset notAfter, DateTimeOffset? asOf = null)
    {
        if (AllowExpiredCertificates)
        {
            return true;
        }

        var instant = asOf ?? DateTimeOffset.UtcNow;
        return instant >= notBefore && instant <= notAfter;
    }
}
