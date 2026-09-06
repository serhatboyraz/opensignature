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
        ProviderId = "smartcard";
        Name = "Smart Card Signing Provider";
    }
}
