using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Pkcs11;

namespace OpenSignature.Signing.Hsm;

/// <summary>
/// <see cref="ISigningProvider"/> for HSMs via PKCS#11 with a bounded session pool and concurrency limits.
/// Private keys never leave the HSM; only digest signing is performed.
/// </summary>
public sealed class HsmSigningProvider : ISigningProvider
{
    private const string ProviderScheme = "hsm";

    private readonly HsmSigningProviderOptions _options;
    private readonly IPkcs11LibraryFactory _libraryFactory;
    private readonly ISigningSecretProvider? _secretProvider;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPkcs11Library? _library;
    private Pkcs11SessionPool? _pool;
    private ulong _slotId;
    private string? _initFailureDetail;
    private bool _disposed;

    public HsmSigningProvider(
        Microsoft.Extensions.Options.IOptions<HsmSigningProviderOptions> options,
        IPkcs11LibraryFactory libraryFactory,
        ISigningSecretProvider? secretProvider = null)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)), libraryFactory, secretProvider)
    {
    }

    public HsmSigningProvider(
        HsmSigningProviderOptions options,
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

        if (options.MaxConcurrentSessions < 1)
        {
            throw new ArgumentException("MaxConcurrentSessions must be at least 1.", nameof(options));
        }

        _options = options;
        _libraryFactory = libraryFactory;
        _secretProvider = secretProvider;

        ProviderId = options.ProviderId.Trim();
        Name = options.Name.Trim();
        ProviderType = SigningProviderType.Hsm;
    }

    public string ProviderId { get; }

    public string Name { get; }

    public SigningProviderType ProviderType { get; }

    public async Task<IReadOnlyList<CertificateInfo>> ListCertificatesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return await WithSessionAsync(
            session => Task.FromResult((IReadOnlyList<CertificateInfo>)EnumerateCertificates(session)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CertificateInfo?> GetCertificateAsync(
        SigningCertificateSelector selector,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(selector);
        cancellationToken.ThrowIfCancellationRequested();

        return await WithSessionAsync(
            session =>
            {
                var match = EnumerateCertificates(session)
                    .FirstOrDefault(c => Pkcs11CertificateMapper.Matches(c, selector));
                return Task.FromResult(match);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> SignDigestAsync(
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

        var digestCopy = digest.ToArray();

        return await WithSessionAsync(
            session =>
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
                    ?? throw new InvalidOperationException("Matching private key was not found on the HSM.");

                return Task.FromResult(session.SignDigest(privateKey, digestCopy, digestAlgorithm));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var pool = await EnsurePoolAsync(cancellationToken).ConfigureAwait(false);
            var library = _library!;
            if (!library.IsAvailable)
            {
                return ProviderHealthStatus.Unavailable(
                    library.UnavailableDetail ?? $"HSM module '{_options.ModulePath}' is unavailable.");
            }

            var certificates = await ListCertificatesAsync(cancellationToken).ConfigureAwait(false);
            var signable = certificates.Count(static c => c.CanSign);
            var available = pool.AvailablePermits;
            var created = pool.CreatedSessionCount;

            if (certificates.Count == 0)
            {
                return ProviderHealthStatus.Degraded(
                    $"HSM provider '{ProviderId}' connected (pool {available}/{pool.MaxSessions} free, {created} created) but found no certificates.");
            }

            if (signable == 0)
            {
                return ProviderHealthStatus.Degraded(
                    $"HSM provider '{ProviderId}' found {certificates.Count} certificate(s) but none are signable (pool {available}/{pool.MaxSessions} free).");
            }

            if (available == 0)
            {
                return ProviderHealthStatus.Degraded(
                    $"HSM provider '{ProviderId}' is at concurrency capacity ({pool.MaxSessions} sessions in use).");
            }

            return ProviderHealthStatus.Healthy(
                $"HSM provider '{ProviderId}' slot {_slotId}: {certificates.Count} certificate(s); {signable} signable; pool {available}/{pool.MaxSessions} free ({created} created).");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or ObjectDisposedException)
        {
            _initFailureDetail ??= Pkcs11ProviderHelpers.SanitizeFailure(ex);
            return ProviderHealthStatus.Unavailable(_initFailureDetail);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _pool?.Dispose();
            _pool = null;
            _library?.Dispose();
            _library = null;
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
        }
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
        var now = DateTimeOffset.UtcNow;
        var result = new List<CertificateInfo>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            result.Add(Pkcs11CertificateMapper.ToCertificateInfo(
                objects[i],
                _slotId,
                ProviderScheme,
                i,
                now));
        }

        return result;
    }

    private async Task<T> WithSessionAsync<T>(
        Func<IPkcs11Session, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var pool = await EnsurePoolAsync(cancellationToken).ConfigureAwait(false);
        var session = await pool.RentAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(session).ConfigureAwait(false);
        }
        finally
        {
            pool.Return(session);
        }
    }

    private async Task<Pkcs11SessionPool> EnsurePoolAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_pool is not null)
        {
            return _pool;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_pool is not null)
            {
                return _pool;
            }

            if (_initFailureDetail is not null)
            {
                throw new InvalidOperationException(
                    $"HSM provider '{ProviderId}' is unavailable: {_initFailureDetail}");
            }

            try
            {
                _library = _libraryFactory.Load(_options.ModulePath.Trim());
                if (!_library.IsAvailable)
                {
                    _initFailureDetail = _library.UnavailableDetail
                        ?? $"HSM module '{_options.ModulePath}' is unavailable.";
                    _library.Dispose();
                    _library = null;
                    throw new InvalidOperationException(
                        $"HSM provider '{ProviderId}' is unavailable: {_initFailureDetail}");
                }

                var slot = Pkcs11ProviderHelpers.ResolveSlot(_library, _options);
                _slotId = slot.SlotId;
                var pin = Pkcs11ProviderHelpers.ResolvePin(_options, _secretProvider);
                _pool = new Pkcs11SessionPool(slot, pin, _options.MaxConcurrentSessions);
                return _pool;
            }
            catch (Exception ex) when (ex is not InvalidOperationException || _initFailureDetail is null)
            {
                _initFailureDetail = Pkcs11ProviderHelpers.SanitizeFailure(ex);
                _pool?.Dispose();
                _pool = null;
                _library?.Dispose();
                _library = null;
                throw new InvalidOperationException(
                    $"HSM provider '{ProviderId}' is unavailable: {_initFailureDetail}",
                    ex);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
