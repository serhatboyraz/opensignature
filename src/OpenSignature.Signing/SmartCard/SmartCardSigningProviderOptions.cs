namespace OpenSignature.Signing.SmartCard;

/// <summary>
/// Configuration for <see cref="SmartCardSigningProvider"/>.
/// PIN is resolved only via <see cref="Pkcs11.Pkcs11ProviderOptionsBase.PinSecretName"/>.
/// </summary>
public sealed class SmartCardSigningProviderOptions : Pkcs11.Pkcs11ProviderOptionsBase
{
    public const string SectionName = "Signing:SmartCard";

    public SmartCardSigningProviderOptions()
    {
        ProviderId = "usb-token";
        Name = "USB Token / Smart Card";
    }

    /// <summary>
    /// When true and <see cref="Pkcs11.Pkcs11ProviderOptionsBase.ModulePath"/> is empty,
    /// probe well-known vendor PKCS#11 library locations (USB token middleware).
    /// If nothing is found, a sentinel path is used so the provider still appears in the API as unavailable.
    /// </summary>
    public bool AutoDetect { get; set; }
}
