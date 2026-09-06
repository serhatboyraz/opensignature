using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Pkcs11;

namespace OpenSignature.Signing.SmartCard;

/// <summary>
/// <see cref="ISigningProvider"/> for USB tokens / smart cards via PKCS#11.
/// Opens a short-lived session per operation (open → login → use → close).
/// Private keys never leave the token; only digest signing is performed.
/// </summary>
public sealed class SmartCardSigningProvider : ISigningProvider
{
    private const string ProviderScheme = "smartcard";

    private readonly SmartCardSigningProviderOptions _options;
    private readonly IPkcs11LibraryFactory _libraryFactory;
    private readonly ISigningSecretProvider? _secretProvider;
    private readonly object _gate = new();
    private IPkcs11Library? _library;
    private string? _initFailureDetail;
    private bool _disposed;

    public SmartCardSigningProvider(
        Microsoft.Extensions.Options.IOptions<SmartCardSigningProviderOptions> options,
        IPkcs11LibraryFactory libraryFactory,
        ISigningSecretProvider? secretProvider = null)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)), libraryFactory, secretProvider)
    {
    }

    public SmartCardSigningProvider(
        SmartCardSigningProviderOptions options,
        IPkcs11LibraryFactory libraryFactory,
        ISigningSecretProvider? secretProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(libraryFactory);

        if (string.IsNullOrWhiteSpace(options.ProviderId))
        {
            throw new ArgumentException("Provider id must not be empty.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new ArgumentException("Provider name must not be empty.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ModulePath))
        {
            throw new ArgumentException("ModulePath must not be empty.", nameof(options));
        }

        _options = options;
        _libraryFactory = libraryFactory;
        _secretProvider = secretProvider;

        ProviderId = options.ProviderId.Trim();
        Name = options.Name.Trim();
        ProviderType = SigningProviderType.SmartCard;
    }

    public string ProviderId { get; }

    public string Name { get; }

    public SigningProviderType ProviderType { get; }

    public Task<IReadOnlyList<CertificateInfo>> ListCertificatesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(WithSession(session =>
        {
            var certificates = EnumerateCertificates(session);
            return (IReadOnlyList<CertificateInfo>)certificates;
        }));
    }

    public Task<CertificateInfo?> GetCertificateAsync(
        SigningCertificateSelector selector,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(selector);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(WithSession(session =>
        {
            var match = EnumerateCertificates(session)
                .FirstOrDefault(c => Pkcs11CertificateMapper.Matches(c, selector));
            return match;
        }));
    }

    public Task<byte[]> SignDigestAsync(
        ReadOnlyMemory<byte> digest,
        DigestAlgorithm digestAlgorithm,
        SigningCertificateSelector certificateSelector,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(certificateSelector);
        cancellationToken.ThrowIfCancellationRequested();

        var expectedLength = digestAlgorithm.GetDigestLengthBytes();
        if (digest.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Digest length {digest.Length} does not match {digestAlgorithm} expected length {expectedLength}.",
                nameof(digest));
        }

        return Task.FromResult(WithSession(session =>
        {
            var objects = FindCertificateObjects(session);
            var infos = MapCertificates(objects);
            var index = infos.FindIndex(c => Pkcs11CertificateMapper.Matches(c, certificateSelector));
            if (index < 0)
            {
                throw new InvalidOperationException("Signing certificate was not found for the provided selector.");
            }

            if (!infos[index].CanSign)
            {
                throw new InvalidOperationException(
                    "Selected certificate cannot sign (missing private key pairing or outside validity window).");
            }

            var keyFilter = Pkcs11CertificateMapper.CreateKeyFilter(objects[index]);
            var privateKey = session.FindPrivateKey(keyFilter)
                ?? throw new InvalidOperationException("Matching private key was not found on the token.");

            return session.SignDigest(privateKey, digest.Span, digestAlgorithm);
        }));
    }

    public Task<ProviderHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var library = EnsureLibrary();
            if (!library.IsAvailable)
            {
                return Task.FromResult(ProviderHealthStatus.Unavailable(
                    library.UnavailableDetail ?? $"Smart card module '{_options.ModulePath}' is unavailable."));
            }

            var slot = Pkcs11ProviderHelpers.ResolveSlot(library, _options);
            if (!slot.TokenPresent)
            {
                return Task.FromResult(ProviderHealthStatus.Unavailable(
                    $"Smart card token is not present in slot '{slot.SlotId}'."));
            }

            var certificates = WithSession(session => EnumerateCertificates(session));
            var signable = certificates.Count(static c => c.CanSign);
            if (certificates.Count == 0)
            {
                return Task.FromResult(ProviderHealthStatus.Degraded(
                    $"Smart card provider '{ProviderId}' connected but found no certificates."));
            }

            if (signable == 0)
            {
                return Task.FromResult(ProviderHealthStatus.Degraded(
                    $"Smart card provider '{ProviderId}' found {certificates.Count} certificate(s) but none are signable."));
            }

            return Task.FromResult(ProviderHealthStatus.Healthy(
                $"Smart card provider '{ProviderId}' slot {slot.SlotId}: {certificates.Count} certificate(s); {signable} signable."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            _initFailureDetail ??= Pkcs11ProviderHelpers.SanitizeFailure(ex);
            return Task.FromResult(ProviderHealthStatus.Unavailable(_initFailureDetail));
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        lock (_gate)
        {
            _library?.Dispose();
            _library = null;
        }

        return ValueTask.CompletedTask;
    }

    private List<CertificateInfo> EnumerateCertificates(IPkcs11Session session) =>
        MapCertificates(FindCertificateObjects(session));

    private IReadOnlyList<Pkcs11CertificateObject> FindCertificateObjects(IPkcs11Session session)
    {
        var filter = Pkcs11CertificateMapper.CreateConfiguredFilter(_options);
        return session.FindCertificates(filter);
    }

    private List<CertificateInfo> MapCertificates(IReadOnlyList<Pkcs11CertificateObject> objects)
    {
        var library = EnsureLibrary();
        var slot = Pkcs11ProviderHelpers.ResolveSlot(library, _options);
        var now = DateTimeOffset.UtcNow;
        var result = new List<CertificateInfo>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            result.Add(Pkcs11CertificateMapper.ToCertificateInfo(
                objects[i],
                slot.SlotId,
                ProviderScheme,
                i,
                now));
        }

        return result;
    }

    private T WithSession<T>(Func<IPkcs11Session, T> action)
    {
        var library = EnsureLibrary();
        var slot = Pkcs11ProviderHelpers.ResolveSlot(library, _options);
        var pin = Pkcs11ProviderHelpers.ResolvePin(_options, _secretProvider);

        using var session = slot.OpenSession(readWrite: false);
        try
        {
            session.Login(pin);
            return action(session);
        }
        finally
        {
            try
            {
                if (session.IsLoggedIn)
                {
                    session.Logout();
                }
            }
            catch (InvalidOperationException)
            {
                // Best-effort logout during cleanup.
            }
        }
    }

    private IPkcs11Library EnsureLibrary()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_library is not null)
            {
                return _library;
            }

            if (_initFailureDetail is not null)
            {
                throw new InvalidOperationException(
                    $"Smart card provider '{ProviderId}' is unavailable: {_initFailureDetail}");
            }

            try
            {
                _library = _libraryFactory.Load(_options.ModulePath.Trim());
                if (!_library.IsAvailable)
                {
                    _initFailureDetail = _library.UnavailableDetail
                        ?? $"Smart card module '{_options.ModulePath}' is unavailable.";
                    _library.Dispose();
                    _library = null;
                    throw new InvalidOperationException(
                        $"Smart card provider '{ProviderId}' is unavailable: {_initFailureDetail}");
                }

                return _library;
            }
            catch (Exception ex) when (ex is not InvalidOperationException || _initFailureDetail is null)
            {
                _initFailureDetail = Pkcs11ProviderHelpers.SanitizeFailure(ex);
                throw new InvalidOperationException(
                    $"Smart card provider '{ProviderId}' is unavailable: {_initFailureDetail}",
                    ex);
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
