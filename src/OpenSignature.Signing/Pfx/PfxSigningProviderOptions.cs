namespace OpenSignature.Signing.Pfx;

/// <summary>
/// Configuration for <see cref="PfxSigningProvider"/>.
/// Supply either <see cref="Path"/> or <see cref="CertificateBytes"/> (not both).
/// Password may come from <see cref="Password"/> (development) or
/// <see cref="PasswordSecretName"/> resolved via <see cref="ISigningSecretProvider"/> (T113).
/// </summary>
public sealed class PfxSigningProviderOptions
{
    public const string SectionName = "Signing:Pfx";

    /// <summary>Stable provider instance id (configuration key).</summary>
    public string ProviderId { get; set; } = "pfx";

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = "PFX Signing Provider";

    /// <summary>Filesystem path to a PKCS#12 (.pfx/.p12) file. Mutually exclusive with <see cref="CertificateBytes"/>.</summary>
    public string? Path { get; set; }

    /// <summary>Raw PKCS#12 bytes. Mutually exclusive with <see cref="Path"/>.</summary>
    public byte[]? CertificateBytes { get; set; }

    /// <summary>
    /// Development/demo PFX password from configuration.
    /// Prefer <see cref="PasswordSecretName"/> with a secret provider in production (T113).
    /// Never commit real passwords to source control.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional secret name resolved through <see cref="ISigningSecretProvider"/>.
    /// When set, overrides <see cref="Password"/>.
    /// </summary>
    public string? PasswordSecretName { get; set; }
}
