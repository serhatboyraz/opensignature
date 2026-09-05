using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Pfx;

/// <summary>
/// Development/demo <see cref="ISigningProvider"/> backed by a PKCS#12 (PFX) store.
/// Private keys remain inside the loaded certificate objects and are never returned to callers.
/// Never commit real PFX files or passwords to source control.
/// </summary>
public sealed class PfxSigningProvider : ISigningProvider
{
    private readonly List<LoadedEntry> _entries = [];
    private readonly string? _loadFailureDetail;
    private bool _disposed;

    public PfxSigningProvider(
        IOptions<PfxSigningProviderOptions> options,
        ISigningSecretProvider? secretProvider = null)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)), secretProvider)
    {
    }

    public PfxSigningProvider(
        PfxSigningProviderOptions options,
        ISigningSecretProvider? secretProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ProviderId))
        {
            throw new ArgumentException("Provider id must not be empty.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new ArgumentException("Provider name must not be empty.", nameof(options));
        }

        var hasPath = !string.IsNullOrWhiteSpace(options.Path);
        var hasBytes = options.CertificateBytes is { Length: > 0 };
        if (hasPath == hasBytes)
        {
            throw new ArgumentException(
                "Exactly one of Path or CertificateBytes must be provided.",
                nameof(options));
        }

        ProviderId = options.ProviderId.Trim();
        Name = options.Name.Trim();
        ProviderType = SigningProviderType.Pfx;

        string? password;
        try
        {
            password = ResolvePassword(options, secretProvider);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _loadFailureDetail = SanitizeLoadFailure(ex);
            return;
        }

        try
        {
            var pfxBytes = hasBytes
                ? options.CertificateBytes!
                : File.ReadAllBytes(Path.GetFullPath(options.Path!.Trim()));

            LoadCertificates(pfxBytes, password);
        }
        catch (Exception ex) when (
            ex is CryptographicException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            DisposeEntries();
            _loadFailureDetail = SanitizeLoadFailure(ex);
        }
    }

    public string ProviderId { get; }

    public string Name { get; }

    public SigningProviderType ProviderType { get; }

    public Task<IReadOnlyList<CertificateInfo>> ListCertificatesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLoaded();

        IReadOnlyList<CertificateInfo> certificates = _entries.Select(static e => e.Info).ToArray();
        return Task.FromResult(certificates);
    }

    public Task<CertificateInfo?> GetCertificateAsync(
        SigningCertificateSelector selector,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(selector);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLoaded();

        var match = FindEntry(selector);
        return Task.FromResult(match?.Info);
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
        EnsureLoaded();

        var expectedLength = digestAlgorithm.GetDigestLengthBytes();
        if (digest.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Digest length {digest.Length} does not match {digestAlgorithm} expected length {expectedLength}.",
                nameof(digest));
        }

        var entry = FindEntry(certificateSelector)
            ?? throw new InvalidOperationException("Signing certificate was not found for the provided selector.");

        if (!entry.Info.CanSign)
        {
            throw new InvalidOperationException(
                "Selected certificate cannot sign (missing private key, unsupported algorithm, or outside validity window).");
        }

        var hashName = ToHashAlgorithmName(digestAlgorithm);
        var digestBytes = digest.ToArray();

        using var rsa = entry.Certificate.GetRSAPrivateKey();
        if (rsa is not null)
        {
            var signature = rsa.SignHash(digestBytes, hashName, RSASignaturePadding.Pkcs1);
            return Task.FromResult(signature);
        }

        using var ecdsa = entry.Certificate.GetECDsaPrivateKey();
        if (ecdsa is not null)
        {
            var signature = ecdsa.SignHash(digestBytes);
            return Task.FromResult(signature);
        }

        throw new InvalidOperationException(
            "Selected certificate has no usable RSA or ECDSA private key for digest signing.");
    }

    public Task<ProviderHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (_loadFailureDetail is not null)
        {
            return Task.FromResult(ProviderHealthStatus.Unavailable(_loadFailureDetail));
        }

        var signableCount = _entries.Count(static e => e.Info.CanSign);
        if (_entries.Count == 0)
        {
            return Task.FromResult(ProviderHealthStatus.Degraded(
                $"PFX provider '{ProviderId}' loaded successfully but contains no certificates."));
        }

        if (signableCount == 0)
        {
            return Task.FromResult(ProviderHealthStatus.Degraded(
                $"PFX provider '{ProviderId}' loaded {_entries.Count} certificate(s) but none are currently signable."));
        }

        return Task.FromResult(ProviderHealthStatus.Healthy(
            $"PFX provider '{ProviderId}' loaded {_entries.Count} certificate(s); {signableCount} signable."));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        DisposeEntries();
        return ValueTask.CompletedTask;
    }

    private void LoadCertificates(byte[] pfxBytes, string? password)
    {
        var collection = X509CertificateLoader.LoadPkcs12Collection(
            pfxBytes,
            password,
            X509KeyStorageFlags.EphemeralKeySet);

        var now = DateTimeOffset.UtcNow;
        var index = 0;
        try
        {
            foreach (var certificate in collection)
            {
                var info = CreateCertificateInfo(certificate, index, now);
                _entries.Add(new LoadedEntry(info, certificate));
                index++;
            }
        }
        catch
        {
            DisposeEntries();
            foreach (var leftover in collection)
            {
                if (_entries.TrueForAll(entry => !ReferenceEquals(entry.Certificate, leftover)))
                {
                    leftover.Dispose();
                }
            }

            throw;
        }
    }

    private static CertificateInfo CreateCertificateInfo(
        X509Certificate2 certificate,
        int index,
        DateTimeOffset asOf)
    {
        var thumbprint = CertificateThumbprint.Create(certificate.Thumbprint);
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
        var currentlyValid = asOf >= notBefore && asOf <= notAfter;

        string? publicKeyAlgorithm = null;
        int? keySizeBits = null;
        var hasUsablePrivateKey = false;

        using (var rsa = certificate.GetRSAPrivateKey())
        using (var ecdsa = certificate.GetECDsaPrivateKey())
        {
            if (rsa is not null)
            {
                hasUsablePrivateKey = true;
                publicKeyAlgorithm = "RSA";
                keySizeBits = rsa.KeySize;
            }
            else if (ecdsa is not null)
            {
                hasUsablePrivateKey = true;
                publicKeyAlgorithm = "ECDSA";
                keySizeBits = ecdsa.KeySize;
            }
        }

        if (publicKeyAlgorithm is null)
        {
            using var rsaPublic = certificate.GetRSAPublicKey();
            using var ecdsaPublic = certificate.GetECDsaPublicKey();
            if (rsaPublic is not null)
            {
                publicKeyAlgorithm = "RSA";
                keySizeBits = rsaPublic.KeySize;
            }
            else if (ecdsaPublic is not null)
            {
                publicKeyAlgorithm = "ECDSA";
                keySizeBits = ecdsaPublic.KeySize;
            }
        }

        var canSign = certificate.HasPrivateKey && hasUsablePrivateKey && currentlyValid;
        var publicDer = certificate.Export(X509ContentType.Cert);
        var keyUsages = ReadKeyUsages(certificate);
        var enhancedKeyUsages = ReadEnhancedKeyUsages(certificate);

        return new CertificateInfo(
            thumbprint: thumbprint,
            subject: certificate.Subject,
            issuer: certificate.Issuer,
            serialNumber: certificate.SerialNumber,
            notBefore: notBefore,
            notAfter: notAfter,
            providerReference: $"pfx://{index}/{thumbprint.Value}",
            canSign: canSign,
            publicCertificateDer: publicDer,
            friendlyName: string.IsNullOrWhiteSpace(certificate.FriendlyName) ? null : certificate.FriendlyName,
            publicKeyAlgorithm: publicKeyAlgorithm,
            keySizeBits: keySizeBits,
            keyUsages: keyUsages,
            enhancedKeyUsages: enhancedKeyUsages);
    }

    private static IReadOnlyList<string> ReadKeyUsages(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (extension is null)
        {
            return Array.Empty<string>();
        }

        var usages = new List<string>();
        var flags = extension.KeyUsages;
        if (flags.HasFlag(X509KeyUsageFlags.DigitalSignature))
        {
            usages.Add(nameof(X509KeyUsageFlags.DigitalSignature));
        }

        if (flags.HasFlag(X509KeyUsageFlags.NonRepudiation))
        {
            usages.Add(nameof(X509KeyUsageFlags.NonRepudiation));
        }

        if (flags.HasFlag(X509KeyUsageFlags.KeyEncipherment))
        {
            usages.Add(nameof(X509KeyUsageFlags.KeyEncipherment));
        }

        if (flags.HasFlag(X509KeyUsageFlags.DataEncipherment))
        {
            usages.Add(nameof(X509KeyUsageFlags.DataEncipherment));
        }

        if (flags.HasFlag(X509KeyUsageFlags.KeyAgreement))
        {
            usages.Add(nameof(X509KeyUsageFlags.KeyAgreement));
        }

        if (flags.HasFlag(X509KeyUsageFlags.KeyCertSign))
        {
            usages.Add(nameof(X509KeyUsageFlags.KeyCertSign));
        }

        if (flags.HasFlag(X509KeyUsageFlags.CrlSign))
        {
            usages.Add(nameof(X509KeyUsageFlags.CrlSign));
        }

        return usages.Count == 0 ? Array.Empty<string>() : usages;
    }

    private static IReadOnlyList<string> ReadEnhancedKeyUsages(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (extension is null || extension.EnhancedKeyUsages.Count == 0)
        {
            return Array.Empty<string>();
        }

        return extension.EnhancedKeyUsages
            .Cast<Oid>()
            .Select(static oid => oid.Value ?? oid.FriendlyName ?? string.Empty)
            .Where(static value => value.Length > 0)
            .ToArray();
    }

    private LoadedEntry? FindEntry(SigningCertificateSelector selector)
    {
        return _entries.FirstOrDefault(entry => Matches(entry.Info, selector));
    }

    private static bool Matches(CertificateInfo certificate, SigningCertificateSelector selector)
    {
        if (selector.Thumbprint is not null
            && !string.Equals(certificate.Thumbprint.Value, selector.Thumbprint.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.ProviderReference is not null
            && !string.Equals(certificate.ProviderReference, selector.ProviderReference, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.SerialNumber is not null)
        {
            if (!string.Equals(certificate.SerialNumber, selector.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (selector.Issuer is not null
                && !string.Equals(certificate.Issuer, selector.Issuer, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (selector.SubjectContains is not null
            && !certificate.Subject.Contains(selector.SubjectContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return selector.Thumbprint is not null
            || selector.ProviderReference is not null
            || selector.SerialNumber is not null
            || selector.SubjectContains is not null;
    }

    private static string? ResolvePassword(
        PfxSigningProviderOptions options,
        ISigningSecretProvider? secretProvider)
    {
        if (!string.IsNullOrWhiteSpace(options.PasswordSecretName))
        {
            if (secretProvider is null)
            {
                throw new InvalidOperationException(
                    "PasswordSecretName is configured but no ISigningSecretProvider was supplied.");
            }

            var secret = secretProvider.GetSecret(options.PasswordSecretName.Trim());
            if (secret is null)
            {
                throw new InvalidOperationException(
                    $"PFX password secret '{options.PasswordSecretName.Trim()}' was not found.");
            }

            return secret;
        }

        return options.Password;
    }

    private static string SanitizeLoadFailure(Exception ex)
    {
        // Never include exception messages that might echo passwords or file contents.
        return ex switch
        {
            CryptographicException => "Failed to load PKCS#12 material (invalid password or corrupt PFX).",
            FileNotFoundException => "Configured PFX path was not found.",
            DirectoryNotFoundException => "Configured PFX path directory was not found.",
            UnauthorizedAccessException => "Access to the configured PFX path was denied.",
            IOException => "Failed to read the configured PFX path.",
            InvalidOperationException => "Failed to resolve the PFX password secret.",
            ArgumentException => "PFX provider configuration is invalid.",
            _ => "Failed to load PKCS#12 material."
        };
    }

    private static HashAlgorithmName ToHashAlgorithmName(DigestAlgorithm algorithm) =>
        algorithm switch
        {
            DigestAlgorithm.Sha256 => HashAlgorithmName.SHA256,
            DigestAlgorithm.Sha384 => HashAlgorithmName.SHA384,
            DigestAlgorithm.Sha512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
        };

    private void EnsureLoaded()
    {
        if (_loadFailureDetail is not null)
        {
            throw new InvalidOperationException(
                $"PFX provider '{ProviderId}' is unavailable: {_loadFailureDetail}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void DisposeEntries()
    {
        foreach (var entry in _entries)
        {
            entry.Certificate.Dispose();
        }

        _entries.Clear();
    }

    private sealed class LoadedEntry(CertificateInfo info, X509Certificate2 certificate)
    {
        public CertificateInfo Info { get; } = info;

        public X509Certificate2 Certificate { get; } = certificate;
    }
}
