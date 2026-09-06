using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using OpenSignature.Signing.Contracts;
using InteropSession = Net.Pkcs11Interop.HighLevelAPI.ISession;

namespace OpenSignature.Signing.Pkcs11.Native;

/// <summary>
/// Native PKCS#11 session. Private key material never leaves the device.
/// </summary>
internal sealed class NativePkcs11Session : IPkcs11Session
{
    private readonly InteropSession _session;
    private bool _disposed;
    private bool _loggedIn;

    public NativePkcs11Session(InteropSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    public bool IsLoggedIn => _loggedIn;

    public void Login(string pin)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(pin))
        {
            throw new ArgumentException("PIN must not be empty.", nameof(pin));
        }

        try
        {
            _session.Login(CKU.CKU_USER, pin);
            _loggedIn = true;
        }
        catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN)
        {
            _loggedIn = true;
        }
        catch (Pkcs11Exception)
        {
            throw new InvalidOperationException("PKCS#11 login failed (invalid PIN or locked token).");
        }
    }

    public void Logout()
    {
        ThrowIfDisposed();
        if (!_loggedIn)
        {
            return;
        }

        try
        {
            _session.Logout();
        }
        catch (Pkcs11Exception)
        {
            // Best-effort logout.
        }

        _loggedIn = false;
    }

    public IReadOnlyList<Pkcs11CertificateObject> FindCertificates(Pkcs11ObjectFilter? filter = null)
    {
        ThrowIfDisposed();

        var found = FindCertificateHandles();
        if (found.Count == 0)
        {
            return Array.Empty<Pkcs11CertificateObject>();
        }

        var keyIds = TryReadPrivateKeyIds();
        var keyLabels = TryReadPrivateKeyLabels();
        var effectiveFilter = filter ?? new Pkcs11ObjectFilter();
        var result = new List<Pkcs11CertificateObject>(found.Count);

        foreach (var handle in found)
        {
            if (!TryReadCertificate(handle, out var der, out var label, out var id))
            {
                continue;
            }

            if (!effectiveFilter.Matches(label, id))
            {
                continue;
            }

            var hasKey = (id.Length > 0 && ContainsId(keyIds, id))
                || (!string.IsNullOrWhiteSpace(label) && keyLabels.Contains(label));
            result.Add(new Pkcs11CertificateObject(der, label, id, hasKey));
        }

        return result;
    }

    public Pkcs11PrivateKeyHandle? FindPrivateKey(Pkcs11ObjectFilter filter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(filter);

        var factory = _session.Factories.ObjectAttributeFactory;
        var template = new List<IObjectAttribute>
        {
            factory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY)
        };

        if (filter.Id is { Length: > 0 })
        {
            template.Add(factory.Create(CKA.CKA_ID, filter.Id));
        }

        if (!string.IsNullOrWhiteSpace(filter.Label))
        {
            template.Add(factory.Create(CKA.CKA_LABEL, filter.Label.Trim()));
        }

        IReadOnlyList<IObjectHandle> found;
        try
        {
            found = _session.FindAllObjects(template);
        }
        catch (Pkcs11Exception)
        {
            return null;
        }

        if (found.Count == 0)
        {
            return null;
        }

        var handle = found[0];
        TryReadLabelAndId(handle, out var label, out var id);
        return new Pkcs11PrivateKeyHandle(ReadKeyType(handle), label, id, handle);
    }

    public byte[] SignDigest(
        Pkcs11PrivateKeyHandle privateKey,
        ReadOnlySpan<byte> digest,
        DigestAlgorithm digestAlgorithm)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(privateKey);
        EnsureLoggedIn();

        if (privateKey.SessionLocalId is not IObjectHandle handle)
        {
            throw new InvalidOperationException("Private key handle is not valid for this session.");
        }

        var expectedLength = digestAlgorithm.GetDigestLengthBytes();
        if (digest.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Digest length {digest.Length} does not match {digestAlgorithm} expected length {expectedLength}.",
                nameof(digest));
        }

        try
        {
            if (string.Equals(privateKey.KeyType, "RSA", StringComparison.OrdinalIgnoreCase))
            {
                var mechanism = _session.Factories.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
                var digestInfo = RsaPkcs1DigestInfo.Wrap(digest, digestAlgorithm);
                return _session.Sign(mechanism, handle, digestInfo);
            }

            var ecdsa = _session.Factories.MechanismFactory.Create(CKM.CKM_ECDSA);
            return _session.Sign(ecdsa, handle, digest.ToArray());
        }
        catch (Pkcs11Exception)
        {
            throw new InvalidOperationException("PKCS#11 digest signing failed.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_loggedIn)
            {
                _session.Logout();
            }
        }
        catch (Pkcs11Exception)
        {
            // Best-effort logout.
        }

        _loggedIn = false;
        _session.Dispose();
    }

    private List<byte[]> TryReadPrivateKeyIds()
    {
        var ids = new List<byte[]>();
        foreach (var handle in TryFindPrivateKeys())
        {
            TryReadLabelAndId(handle, out _, out var id);
            if (id.Length > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private HashSet<string> TryReadPrivateKeyLabels()
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in TryFindPrivateKeys())
        {
            TryReadLabelAndId(handle, out var label, out _);
            if (!string.IsNullOrWhiteSpace(label))
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    private IReadOnlyList<IObjectHandle> TryFindPrivateKeys()
    {
        try
        {
            var factory = _session.Factories.ObjectAttributeFactory;
            return _session.FindAllObjects(
            [
                factory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY)
            ]);
        }
        catch (Pkcs11Exception)
        {
            return Array.Empty<IObjectHandle>();
        }
    }

    /// <summary>
    /// Some vendor modules reject CKA_CERTIFICATE_TYPE in the find template (empty result or CKR error).
    /// Fall back to CKO_CERTIFICATE only and let X.509 parsing filter the rest.
    /// </summary>
    private IReadOnlyList<IObjectHandle> FindCertificateHandles()
    {
        var factory = _session.Factories.ObjectAttributeFactory;
        var withType = TryFindObjects(
        [
            factory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
            factory.Create(CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509)
        ]);
        if (withType.Count > 0)
        {
            return withType;
        }

        return TryFindObjects(
        [
            factory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
        ]);
    }

    private IReadOnlyList<IObjectHandle> TryFindObjects(List<IObjectAttribute> template)
    {
        try
        {
            return _session.FindAllObjects(template);
        }
        catch (Pkcs11Exception)
        {
            return Array.Empty<IObjectHandle>();
        }
    }

    private bool TryReadCertificate(IObjectHandle handle, out byte[] der, out string? label, out byte[] id)
    {
        der = [];
        label = null;
        id = [];

        // Read attributes separately: a batch GetAttributeValue fails on many USB tokens
        // when any one attribute is missing, empty, or marked sensitive.
        if (!TryReadAttributeBytes(handle, CKA.CKA_VALUE, out der) || der.Length == 0)
        {
            return false;
        }

        TryReadAttributeString(handle, CKA.CKA_LABEL, out label);
        TryReadAttributeBytes(handle, CKA.CKA_ID, out id);
        return true;
    }

    private bool TryReadAttributeBytes(IObjectHandle handle, CKA attribute, out byte[] value)
    {
        value = [];
        try
        {
            var attributes = _session.GetAttributeValue(handle, new List<CKA> { attribute });
            return attributes.Count > 0 && TryGetBytes(attributes[0], out value);
        }
        catch (Exception ex) when (ex is Pkcs11Exception or ArgumentException or InvalidCastException)
        {
            value = [];
            return false;
        }
    }

    private bool TryReadAttributeString(IObjectHandle handle, CKA attribute, out string? value)
    {
        value = null;
        try
        {
            var attributes = _session.GetAttributeValue(handle, new List<CKA> { attribute });
            return attributes.Count > 0 && TryGetString(attributes[0], out value);
        }
        catch (Exception ex) when (ex is Pkcs11Exception or ArgumentException or InvalidCastException)
        {
            value = null;
            return false;
        }
    }

    private void TryReadLabelAndId(IObjectHandle handle, out string? label, out byte[] id)
    {
        TryReadAttributeString(handle, CKA.CKA_LABEL, out label);
        if (!TryReadAttributeBytes(handle, CKA.CKA_ID, out id))
        {
            id = [];
        }
    }

    private string ReadKeyType(IObjectHandle handle)
    {
        try
        {
            var attributes = _session.GetAttributeValue(handle, new List<CKA> { CKA.CKA_KEY_TYPE });
            if (attributes.Count > 0)
            {
                var value = attributes[0].GetValueAsUlong();
                if (value == (ulong)CKK.CKK_RSA)
                {
                    return "RSA";
                }
            }
        }
        catch (Pkcs11Exception)
        {
            // Fall through to ECDSA as a conservative default for non-RSA keys.
        }

        return "ECDSA";
    }

    private static bool TryGetBytes(IObjectAttribute attribute, out byte[] value)
    {
        try
        {
            if (attribute.CannotBeRead)
            {
                value = [];
                return false;
            }

            var bytes = attribute.GetValueAsByteArray();
            value = bytes is { Length: > 0 } ? bytes : [];
            return value.Length > 0;
        }
        catch (Exception ex) when (ex is Pkcs11Exception or ArgumentException or InvalidCastException)
        {
            value = [];
            return false;
        }
    }

    private static bool TryGetString(IObjectAttribute attribute, out string? value)
    {
        try
        {
            if (attribute.CannotBeRead)
            {
                value = null;
                return false;
            }

            var text = attribute.GetValueAsString();
            value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            return value is not null;
        }
        catch (Exception ex) when (ex is Pkcs11Exception or ArgumentException or InvalidCastException)
        {
            value = null;
            return false;
        }
    }

    private static bool ContainsId(List<byte[]> ids, byte[] id)
    {
        foreach (var candidate in ids)
        {
            if (candidate.AsSpan().SequenceEqual(id))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureLoggedIn()
    {
        if (!_loggedIn)
        {
            throw new InvalidOperationException("PKCS#11 session is not logged in.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
