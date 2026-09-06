namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Opaque handle to a private key object that remains inside the PKCS#11 device/session.
/// Callers may only pass this handle to <see cref="IPkcs11Session.SignDigest"/>; private key material is never exposed.
/// </summary>
public sealed class Pkcs11PrivateKeyHandle
{
    public Pkcs11PrivateKeyHandle(
        string keyType,
        string? label = null,
        byte[]? id = null,
        object? sessionLocalId = null)
    {
        if (string.IsNullOrWhiteSpace(keyType))
        {
            throw new ArgumentException("Key type must not be empty.", nameof(keyType));
        }

        KeyType = keyType.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Id = id is null || id.Length == 0 ? Array.Empty<byte>() : id.ToArray();
        SessionLocalId = sessionLocalId;
    }

    /// <summary>Logical key algorithm (e.g. RSA, ECDSA).</summary>
    public string KeyType { get; }

    public string? Label { get; }

    public byte[] Id { get; }

    /// <summary>Backend-specific identifier valid only within the issuing session.</summary>
    public object? SessionLocalId { get; }
}
