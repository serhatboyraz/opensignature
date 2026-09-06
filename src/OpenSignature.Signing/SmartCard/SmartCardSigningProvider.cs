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
    private readonly SigningOptions _signingOptions;
    private readonly object _gate = new();
    private IPkcs11Library? _library;
    private ulong? _resolvedSlotId;
    private string? _initFailureDetail;
    private bool _disposed;

    public SmartCardSigningProvider(
        Microsoft.Extensions.Options.IOptions<SmartCardSigningProviderOptions> options,
        IPkcs11LibraryFactory libraryFactory,
        ISigningSecretProvider? secretProvider = null,
        Microsoft.Extensions.Options.IOptions<SigningOptions>? signingOptions = null)
        : this(
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            libraryFactory,
            secretProvider,
            signingOptions?.Value)
    {
    }

    public SmartCardSigningProvider(
        SmartCardSigningProviderOptions options,
        IPkcs11LibraryFactory libraryFactory,
        ISigningSecretProvider? secretProvider = null,
        SigningOptions? signingOptions = null)
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
        _signingOptions = signingOptions ?? new SigningOptions();

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

        return Task.FromResult(WithSession((session, slot) =>
        {
            var certificates = EnumerateCertificates(session, slot);
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

        return Task.FromResult(WithSession((session, slot) =>
        {
            var match = EnumerateCertificates(session, slot)
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

        return Task.FromResult(WithSession((session, slot) =>
        {
            var mapped = MapCertificatePairs(FindCertificateObjects(session), slot);
            var match = mapped.FirstOrDefault(pair => Pkcs11CertificateMapper.Matches(pair.Info, certificateSelector));
            if (match.Object is null)
            {
                throw new InvalidOperationException("Signing certificate was not found for the provided selector.");
            }

            if (!match.Info.CanSign)
            {
                throw new InvalidOperationException(
                    "Selected certificate cannot sign (missing private key pairing or outside validity window).");
            }

            var keyFilter = Pkcs11CertificateMapper.CreateKeyFilter(match.Object);
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

            IPkcs11Slot slot;
            try
            {
                if (_options.PreferFirstSlotWhenAmbiguous
                    && _options.SlotId is null
                    && string.IsNullOrWhiteSpace(_options.TokenLabel))
                {
                    // Health must not log in — use first token-present slot only.
                    var slots = library.GetSlots(tokenPresentOnly: true);
                    if (slots.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"PKCS#11 module '{_options.ModulePath}' has no slots with a token present.");
                    }

                    slot = slots[0];
                }
                else
                {
                    slot = Pkcs11ProviderHelpers.ResolveSlot(library, _options);
                }
            }
            catch (InvalidOperationException ex)
            {
                return Task.FromResult(ProviderHealthStatus.Unavailable(ex.Message));
            }

            if (!slot.TokenPresent)
            {
                return Task.FromResult(ProviderHealthStatus.Unavailable(
                    $"Smart card token is not present in slot '{slot.SlotId}'."));
            }

            // Do not log in during health checks — repeated PIN attempts can lock USB tokens.
            var label = slot.TokenLabel ?? "unlabeled";
            if (string.IsNullOrWhiteSpace(_options.PinSecretName))
            {
                return Task.FromResult(ProviderHealthStatus.Degraded(
                    $"USB token is present in slot {slot.SlotId} ({label}). Configure PinSecretName to enumerate certificates and sign."));
            }

            try
            {
                _ = Pkcs11ProviderHelpers.ResolvePin(_options, _secretProvider);
            }
            catch (InvalidOperationException)
            {
                return Task.FromResult(ProviderHealthStatus.Degraded(
                    $"USB token is present in slot {slot.SlotId} ({label}). PIN secret '{_options.PinSecretName.Trim()}' is missing or empty."));
            }

            return Task.FromResult(ProviderHealthStatus.Healthy(
                $"USB token is present in slot {slot.SlotId} ({label})."));
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
            _resolvedSlotId = null;
        }

        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<(Pkcs11CertificateObject Object, CertificateInfo Info)> MapCertificatePairs(
        IReadOnlyList<Pkcs11CertificateObject> objects,
        IPkcs11Slot slot) =>
        Pkcs11CertificateMapper.MapAll(
            objects,
            slot.SlotId,
            ProviderScheme,
            DateTimeOffset.UtcNow,
            _signingOptions.AllowExpiredCertificates);

    private List<CertificateInfo> EnumerateCertificates(IPkcs11Session session, IPkcs11Slot slot) =>
        MapCertificatePairs(FindCertificateObjects(session), slot).Select(static pair => pair.Info).ToList();

    private IReadOnlyList<Pkcs11CertificateObject> FindCertificateObjects(IPkcs11Session session)
    {
        var filter = Pkcs11CertificateMapper.CreateConfiguredFilter(_options);
        return session.FindCertificates(filter);
    }

    private T WithSession<T>(Func<IPkcs11Session, IPkcs11Slot, T> action)
    {
        var library = EnsureLibrary();
        var filter = Pkcs11CertificateMapper.CreateConfiguredFilter(_options);
        var slot = Pkcs11ProviderHelpers.ResolveSlotWithCertificates(
            library,
            _options,
            _secretProvider,
            filter,
            ref _resolvedSlotId);
        var pin = Pkcs11ProviderHelpers.ResolvePin(_options, _secretProvider);

        using var session = slot.OpenSession(readWrite: false);
        try
        {
            session.Login(pin);
            return action(session, slot);
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
