namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Public certificate material discovered on a PKCS#11 token.
/// Never includes private key bytes.
/// </summary>
public sealed class Pkcs11CertificateObject
{
    public Pkcs11CertificateObject(
        byte[] certificateDer,
        string? label = null,
        byte[]? id = null,
        bool hasMatchingPrivateKey = false)
    {
        ArgumentNullException.ThrowIfNull(certificateDer);
        if (certificateDer.Length == 0)
        {
            throw new ArgumentException("Certificate DER must not be empty.", nameof(certificateDer));
        }

        CertificateDer = certificateDer.ToArray();
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Id = id is null || id.Length == 0 ? Array.Empty<byte>() : id.ToArray();
        HasMatchingPrivateKey = hasMatchingPrivateKey;
    }

    /// <summary>DER-encoded X.509 public certificate (CKA_VALUE).</summary>
    public byte[] CertificateDer { get; }

    /// <summary>CKA_LABEL when present.</summary>
    public string? Label { get; }

    /// <summary>CKA_ID when present.</summary>
    public byte[] Id { get; }

    /// <summary>
    /// True when the token exposes a private key object that can sign with this certificate.
    /// Does not imply the private key is extractable.
    /// </summary>
    public bool HasMatchingPrivateKey { get; }
}
