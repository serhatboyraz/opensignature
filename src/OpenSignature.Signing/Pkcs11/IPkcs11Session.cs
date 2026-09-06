using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// PKCS#11 session opened against a token slot.
/// Private keys must remain inside the device; only digest signing is exposed.
/// </summary>
public interface IPkcs11Session : IDisposable
{
    /// <summary>True after a successful <see cref="Login"/>.</summary>
    bool IsLoggedIn { get; }

    /// <summary>
    /// Authenticates the user (CKU_USER). Implementations must never log <paramref name="pin"/>.
    /// </summary>
    void Login(string pin);

    /// <summary>Logs out the user when logged in.</summary>
    void Logout();

    /// <summary>Finds certificate objects on the token (public material only).</summary>
    IReadOnlyList<Pkcs11CertificateObject> FindCertificates(Pkcs11ObjectFilter? filter = null);

    /// <summary>Finds a private key object matching <paramref name="filter"/> without exporting key material.</summary>
    Pkcs11PrivateKeyHandle? FindPrivateKey(Pkcs11ObjectFilter filter);

    /// <summary>
    /// Signs a pre-computed digest inside the device (C_Sign with a digest mechanism).
    /// Returns raw signature bytes only.
    /// </summary>
    byte[] SignDigest(
        Pkcs11PrivateKeyHandle privateKey,
        ReadOnlySpan<byte> digest,
        DigestAlgorithm digestAlgorithm);
}
