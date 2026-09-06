namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Shared configuration for PKCS#11-backed signing providers.
/// PIN is resolved via <see cref="PinSecretName"/> and must never be logged.
/// </summary>
public abstract class Pkcs11ProviderOptionsBase
{
    /// <summary>Stable provider instance id (configuration key).</summary>
    public string ProviderId { get; set; } = "pkcs11";

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = "PKCS#11 Signing Provider";

    /// <summary>Filesystem path or logical name of the PKCS#11 module (.so / .dll).</summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>Optional slot id. When unset, <see cref="TokenLabel"/> or the first token-present slot is used.</summary>
    public ulong? SlotId { get; set; }

    /// <summary>Optional token label (CKA_LABEL of the token) used when <see cref="SlotId"/> is unset.</summary>
    public string? TokenLabel { get; set; }

    /// <summary>
    /// Secret name resolved through <see cref="Pfx.ISigningSecretProvider"/> for the token PIN.
    /// Never log the resolved PIN value.
    /// </summary>
    public string? PinSecretName { get; set; }

    /// <summary>Optional CKA_LABEL filter for the signing certificate / key pair.</summary>
    public string? CertificateLabel { get; set; }

    /// <summary>Optional CKA_ID as hex (e.g. "01AB") for the signing certificate / key pair.</summary>
    public string? CertificateIdHex { get; set; }

    /// <summary>
    /// When true and neither <see cref="SlotId"/> nor <see cref="TokenLabel"/> is set,
    /// choose a usable slot among multiple token-present slots instead of failing.
    /// SafeNet/eToken middleware often exposes several virtual readers; intended for development.
    /// </summary>
    public bool PreferFirstSlotWhenAmbiguous { get; set; }
}
