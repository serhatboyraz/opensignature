namespace OpenSignature.Signing.Hsm;

/// <summary>
/// Configuration for <see cref="HsmSigningProvider"/>.
/// Uses a PKCS#11 session pool with concurrency limits.
/// PIN is resolved only via <see cref="Pkcs11.Pkcs11ProviderOptionsBase.PinSecretName"/>.
/// </summary>
public sealed class HsmSigningProviderOptions : Pkcs11.Pkcs11ProviderOptionsBase
{
    public const string SectionName = "Signing:Hsm";

    public HsmSigningProviderOptions()
    {
        ProviderId = "hsm";
        Name = "HSM Signing Provider";
    }

    /// <summary>
    /// Maximum concurrent PKCS#11 sessions (also the pool capacity and concurrency limit).
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 4;
}
