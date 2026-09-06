namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// PKCS#11 slot that may contain a token (smart card, USB token, or HSM partition).
/// </summary>
public interface IPkcs11Slot
{
    ulong SlotId { get; }

    string? Description { get; }

    string? TokenLabel { get; }

    bool TokenPresent { get; }

    /// <summary>Opens a new session against this slot. Caller owns disposal.</summary>
    IPkcs11Session OpenSession(bool readWrite = false);
}
