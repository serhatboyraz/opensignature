using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Pkcs11.Mock;

/// <summary>
/// In-memory PKCS#11 library for tests. Holds RSA/ECDSA private keys internally and only exposes digest signing.
/// </summary>
public sealed class MockPkcs11Library : IPkcs11Library
{
    private readonly List<MockPkcs11Slot> _slots;
    private bool _disposed;

    private MockPkcs11Library(string modulePath, IEnumerable<MockPkcs11Slot> slots, bool isAvailable, string? unavailableDetail)
    {
        ModulePath = modulePath;
        _slots = slots.ToList();
        IsAvailable = isAvailable;
        UnavailableDetail = unavailableDetail;
    }

    public string ModulePath { get; }

    public bool IsAvailable { get; }

    public string? UnavailableDetail { get; }

    public static MockPkcs11Library CreateUnavailable(string modulePath, string detail) =>
        new(modulePath, Array.Empty<MockPkcs11Slot>(), isAvailable: false, detail);

    /// <summary>
    /// Creates a library with a single slot containing an RSA certificate and matching private key.
    /// The private key never leaves the mock session.
    /// </summary>
    public static MockPkcs11Library CreateWithRsa(
        string modulePath = "mock-pkcs11",
        ulong slotId = 0,
        string tokenLabel = "MockToken",
        string objectLabel = "sign-key",
        byte[]? objectId = null,
        string subject = "CN=OpenSignature Mock PKCS11 RSA",
        string expectedPin = "1234",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        var notBeforeValue = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);
        var notAfterValue = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(notBeforeValue, notAfterValue);
        var der = certificate.Export(X509ContentType.Cert);

        var entry = new MockTokenEntry(
            label: objectLabel,
            id: objectId ?? [0x01],
            certificateDer: der,
            rsa: rsa,
            ecdsa: null,
            expectedPin: expectedPin);

        var slot = new MockPkcs11Slot(slotId, "Mock RSA Slot", tokenLabel, tokenPresent: true, [entry]);
        return new MockPkcs11Library(modulePath, [slot], isAvailable: true, unavailableDetail: null);
    }

    /// <summary>
    /// Creates a library with a single slot containing an ECDSA certificate and matching private key.
    /// </summary>
    public static MockPkcs11Library CreateWithEcdsa(
        string modulePath = "mock-pkcs11",
        ulong slotId = 0,
        string tokenLabel = "MockToken",
        string objectLabel = "sign-key",
        byte[]? objectId = null,
        string subject = "CN=OpenSignature Mock PKCS11 ECDSA",
        string expectedPin = "1234",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        var notBeforeValue = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);
        var notAfterValue = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(notBeforeValue, notAfterValue);
        var der = certificate.Export(X509ContentType.Cert);

        var entry = new MockTokenEntry(
            label: objectLabel,
            id: objectId ?? [0x01],
            certificateDer: der,
            rsa: null,
            ecdsa: ecdsa,
            expectedPin: expectedPin);

        var slot = new MockPkcs11Slot(slotId, "Mock ECDSA Slot", tokenLabel, tokenPresent: true, [entry]);
        return new MockPkcs11Library(modulePath, [slot], isAvailable: true, unavailableDetail: null);
    }

    /// <summary>
    /// Creates a library with an empty virtual reader and a second slot that holds the signing certificate.
    /// Used to exercise PreferFirstSlotWhenAmbiguous probing.
    /// </summary>
    public static MockPkcs11Library CreateWithEmptySlotThenRsa(
        string modulePath = "mock-pkcs11",
        string expectedPin = "1234",
        string subject = "CN=OpenSignature Mock PKCS11 RSA",
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        var empty = new MockPkcs11Slot(0, "Virtual empty", "Empty", tokenPresent: true, []);

        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        var notBeforeValue = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);
        var notAfterValue = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(notBeforeValue, notAfterValue);
        var der = certificate.Export(X509ContentType.Cert);
        var entry = new MockTokenEntry(
            label: "sign-key",
            id: [0x01],
            certificateDer: der,
            rsa: rsa,
            ecdsa: null,
            expectedPin: expectedPin);
        var real = new MockPkcs11Slot(1, "Real token", "RealToken", tokenPresent: true, [entry]);
        return new MockPkcs11Library(modulePath, [empty, real], isAvailable: true, unavailableDetail: null);
    }

    public IReadOnlyList<IPkcs11Slot> GetSlots(bool tokenPresentOnly = true)
    {
        ThrowIfDisposed();
        EnsureAvailable();

        IReadOnlyList<IPkcs11Slot> slots = tokenPresentOnly
            ? _slots.Where(static s => s.TokenPresent).Cast<IPkcs11Slot>().ToArray()
            : _slots.Cast<IPkcs11Slot>().ToArray();
        return slots;
    }

    public IPkcs11Slot GetSlot(ulong slotId)
    {
        ThrowIfDisposed();
        EnsureAvailable();

        return _slots.FirstOrDefault(s => s.SlotId == slotId)
            ?? throw new InvalidOperationException($"PKCS#11 slot '{slotId}' was not found.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var slot in _slots)
        {
            slot.DisposeEntries();
        }

        _slots.Clear();
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                UnavailableDetail ?? $"PKCS#11 module '{ModulePath}' is unavailable.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>
/// Factory that always returns the same mock library instance (tests).
/// </summary>
public sealed class MockPkcs11LibraryFactory : IPkcs11LibraryFactory
{
    private readonly MockPkcs11Library _library;

    public MockPkcs11LibraryFactory(MockPkcs11Library library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public IPkcs11Library Load(string modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new ArgumentException("Module path must not be empty.", nameof(modulePath));
        }

        return _library;
    }
}

internal sealed class MockPkcs11Slot : IPkcs11Slot
{
    private readonly IReadOnlyList<MockTokenEntry> _entries;

    public MockPkcs11Slot(
        ulong slotId,
        string? description,
        string? tokenLabel,
        bool tokenPresent,
        IReadOnlyList<MockTokenEntry> entries)
    {
        SlotId = slotId;
        Description = description;
        TokenLabel = tokenLabel;
        TokenPresent = tokenPresent;
        _entries = entries;
    }

    public ulong SlotId { get; }

    public string? Description { get; }

    public string? TokenLabel { get; }

    public bool TokenPresent { get; }

    public IPkcs11Session OpenSession(bool readWrite = false)
    {
        if (!TokenPresent)
        {
            throw new InvalidOperationException($"PKCS#11 slot '{SlotId}' has no token present.");
        }

        return new MockPkcs11Session(_entries);
    }

    public void DisposeEntries()
    {
        foreach (var entry in _entries)
        {
            entry.Dispose();
        }
    }
}

internal sealed class MockPkcs11Session : IPkcs11Session
{
    private readonly IReadOnlyList<MockTokenEntry> _entries;
    private bool _disposed;
    private bool _loggedIn;

    public MockPkcs11Session(IReadOnlyList<MockTokenEntry> entries)
    {
        _entries = entries;
    }

    public bool IsLoggedIn => _loggedIn;

    public void Login(string pin)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(pin))
        {
            throw new ArgumentException("PIN must not be empty.", nameof(pin));
        }

        // Compare without echoing PIN in exceptions.
        if (_entries.Count == 0 || !_entries.Any(e => e.ValidatePin(pin)))
        {
            throw new InvalidOperationException("PKCS#11 login failed (invalid PIN or locked token).");
        }

        _loggedIn = true;
    }

    public void Logout()
    {
        ThrowIfDisposed();
        _loggedIn = false;
    }

    public IReadOnlyList<Pkcs11CertificateObject> FindCertificates(Pkcs11ObjectFilter? filter = null)
    {
        ThrowIfDisposed();
        EnsureLoggedIn();

        var effectiveFilter = filter ?? new Pkcs11ObjectFilter();
        return _entries
            .Where(e => effectiveFilter.Matches(e.Label, e.Id))
            .Select(e => e.ToCertificateObject())
            .ToArray();
    }

    public Pkcs11PrivateKeyHandle? FindPrivateKey(Pkcs11ObjectFilter filter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(filter);
        EnsureLoggedIn();

        var entry = _entries.FirstOrDefault(e => filter.Matches(e.Label, e.Id) && e.CanSign);
        return entry?.ToPrivateKeyHandle();
    }

    public byte[] SignDigest(
        Pkcs11PrivateKeyHandle privateKey,
        ReadOnlySpan<byte> digest,
        DigestAlgorithm digestAlgorithm)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(privateKey);
        EnsureLoggedIn();

        var expectedLength = digestAlgorithm.GetDigestLengthBytes();
        if (digest.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Digest length {digest.Length} does not match {digestAlgorithm} expected length {expectedLength}.",
                nameof(digest));
        }

        var entry = _entries.FirstOrDefault(e =>
            ReferenceEquals(e, privateKey.SessionLocalId)
            || (privateKey.Id.Length > 0 && e.Id.AsSpan().SequenceEqual(privateKey.Id))
            || (!string.IsNullOrWhiteSpace(privateKey.Label)
                && string.Equals(e.Label, privateKey.Label, StringComparison.Ordinal)));

        if (entry is null || !entry.CanSign)
        {
            throw new InvalidOperationException("Private key handle is not valid for this session.");
        }

        return entry.SignDigest(digest, digestAlgorithm);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loggedIn = false;
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

/// <summary>
/// Token object holding certificate DER and non-exportable in-process private key material for mock signing.
/// </summary>
internal sealed class MockTokenEntry : IDisposable
{
    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;
    private readonly string _expectedPin;
    private bool _disposed;

    public MockTokenEntry(
        string label,
        byte[] id,
        byte[] certificateDer,
        RSA? rsa,
        ECDsa? ecdsa,
        string expectedPin)
    {
        if (rsa is null && ecdsa is null)
        {
            throw new ArgumentException("Mock token entry requires an RSA or ECDSA key.");
        }

        Label = label;
        Id = id.ToArray();
        CertificateDer = certificateDer.ToArray();
        _rsa = rsa;
        _ecdsa = ecdsa;
        _expectedPin = expectedPin;
    }

    public string Label { get; }

    public byte[] Id { get; }

    public byte[] CertificateDer { get; }

    public bool CanSign => !_disposed && (_rsa is not null || _ecdsa is not null);

    public bool ValidatePin(string pin) =>
        string.Equals(pin, _expectedPin, StringComparison.Ordinal);

    public Pkcs11CertificateObject ToCertificateObject() =>
        new(CertificateDer, Label, Id, hasMatchingPrivateKey: CanSign);

    public Pkcs11PrivateKeyHandle ToPrivateKeyHandle() =>
        new(
            keyType: _rsa is not null ? "RSA" : "ECDSA",
            label: Label,
            id: Id,
            sessionLocalId: this);

    public byte[] SignDigest(ReadOnlySpan<byte> digest, DigestAlgorithm digestAlgorithm)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var hashName = digestAlgorithm switch
        {
            DigestAlgorithm.Sha256 => HashAlgorithmName.SHA256,
            DigestAlgorithm.Sha384 => HashAlgorithmName.SHA384,
            DigestAlgorithm.Sha512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(digestAlgorithm), digestAlgorithm, "Unsupported digest algorithm.")
        };

        var digestBytes = digest.ToArray();

        if (_rsa is not null)
        {
            return _rsa.SignHash(digestBytes, hashName, RSASignaturePadding.Pkcs1);
        }

        if (_ecdsa is not null)
        {
            return _ecdsa.SignHash(digestBytes);
        }

        throw new InvalidOperationException("Mock token entry has no private key.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rsa?.Dispose();
        _ecdsa?.Dispose();
    }
}
