namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Loads a PKCS#11 module by path. Production backends load native modules;
/// tests use a mock factory implementation.
/// </summary>
public interface IPkcs11LibraryFactory
{
    /// <summary>Loads (or returns a cached) PKCS#11 library for <paramref name="modulePath"/>.</summary>
    IPkcs11Library Load(string modulePath);
}
