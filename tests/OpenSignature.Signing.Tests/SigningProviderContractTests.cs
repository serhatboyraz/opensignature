using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Tests;

/// <summary>
/// In-memory test double proving <see cref="ISigningProvider"/> is implementable
/// without exposing private key material.
/// </summary>
internal sealed class FakeInMemorySigningProvider : ISigningProvider
{
    private readonly Dictionary<string, CertificateInfo> _certificatesByThumbprint;
    private readonly byte[] _signingSecret;
    private bool _disposed;

    public FakeInMemorySigningProvider(
        string providerId,
        string name,
        SigningProviderType providerType,
        IEnumerable<CertificateInfo> certificates,
        byte[]? signingSecret = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(certificates);

        ProviderId = providerId.Trim();
        Name = name.Trim();
        ProviderType = providerType;
        _signingSecret = signingSecret is { Length: > 0 }
            ? signingSecret.ToArray()
            : "fake-provider-secret"u8.ToArray();
        _certificatesByThumbprint = certificates.ToDictionary(
            certificate => certificate.Thumbprint.Value,
            StringComparer.Ordinal);
    }

    public string ProviderId { get; }

    public string Name { get; }

    public SigningProviderType ProviderType { get; }

    public Task<IReadOnlyList<CertificateInfo>> ListCertificatesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CertificateInfo> certificates = _certificatesByThumbprint.Values.ToArray();
        return Task.FromResult(certificates);
    }

    public Task<CertificateInfo?> GetCertificateAsync(
        SigningCertificateSelector selector,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(selector);
        cancellationToken.ThrowIfCancellationRequested();

        var match = _certificatesByThumbprint.Values.FirstOrDefault(certificate => Matches(certificate, selector));
        return Task.FromResult(match);
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

        var certificate = _certificatesByThumbprint.Values.FirstOrDefault(c => Matches(c, certificateSelector))
            ?? throw new InvalidOperationException("Signing certificate was not found for the provided selector.");

        if (!certificate.CanSign)
        {
            throw new InvalidOperationException("Selected certificate cannot sign.");
        }

        // Deterministic fake signature: HMAC-like mix of secret + digest + thumbprint.
        // Demonstrates digest-in / signature-out without exposing a private key.
        var signature = new byte[expectedLength + 8];
        var digestSpan = digest.Span;
        for (var i = 0; i < digestSpan.Length; i++)
        {
            signature[i] = (byte)(digestSpan[i] ^ _signingSecret[i % _signingSecret.Length]);
        }

        var thumbprintBytes = System.Text.Encoding.ASCII.GetBytes(certificate.Thumbprint.Value);
        for (var i = 0; i < 8; i++)
        {
            signature[expectedLength + i] = thumbprintBytes[i % thumbprintBytes.Length];
        }

        return Task.FromResult(signature);
    }

    public Task<ProviderHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProviderHealthStatus.Healthy($"Fake provider '{ProviderId}' is ready."));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
}

public sealed class SigningProviderContractTests
{
    private static readonly CertificateThumbprint SampleThumbprint =
        CertificateThumbprint.Create(new string('a', 40));

    [Fact]
    public async Task Fake_provider_lists_certificate_and_reports_healthy()
    {
        await using var provider = CreateProvider();

        var certificates = await provider.ListCertificatesAsync();
        var health = await provider.GetHealthAsync();

        Assert.Equal("fake-pfx", provider.ProviderId);
        Assert.Equal("Fake PFX Provider", provider.Name);
        Assert.Equal(SigningProviderType.Pfx, provider.ProviderType);
        Assert.Single(certificates);
        Assert.Equal(SampleThumbprint.Value, certificates[0].Thumbprint.Value);
        Assert.True(certificates[0].CanSign);
        Assert.Empty(certificates[0].PublicCertificateDer);
        Assert.True(health.IsHealthy);
        Assert.Equal(ProviderHealthState.Healthy, health.State);
    }

    [Fact]
    public async Task Fake_provider_resolves_certificate_by_thumbprint_selector()
    {
        await using var provider = CreateProvider();

        var certificate = await provider.GetCertificateAsync(
            SigningCertificateSelector.ByThumbprint(SampleThumbprint));

        Assert.NotNull(certificate);
        Assert.Equal("CN=OpenSignature Test", certificate.Subject);
        Assert.True(certificate.IsCurrentlyValid());
    }

    [Fact]
    public async Task Fake_provider_sign_digest_returns_signature_bytes()
    {
        await using var provider = CreateProvider();
        var digest = new byte[DigestAlgorithm.Sha256.GetDigestLengthBytes()];
        Random.Shared.NextBytes(digest);

        var signature = await provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByProviderReference("fake://cert/1"));

        Assert.NotNull(signature);
        Assert.Equal(digest.Length + 8, signature.Length);
        Assert.NotEqual(digest, signature.AsSpan(0, digest.Length).ToArray());
    }

    [Fact]
    public async Task Fake_provider_rejects_digest_with_wrong_length()
    {
        await using var provider = CreateProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.SignDigestAsync(
            new byte[16],
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByThumbprint(SampleThumbprint)));
    }

    [Fact]
    public void Certificate_info_rejects_inverted_validity_window()
    {
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(-1);

        Assert.Throws<ArgumentException>(() => new CertificateInfo(
            thumbprint: SampleThumbprint,
            subject: "CN=Test",
            issuer: "CN=Issuer",
            serialNumber: "01",
            notBefore: notBefore,
            notAfter: notAfter,
            providerReference: "ref",
            canSign: true));
    }

    [Fact]
    public void Selector_requires_at_least_one_criterion()
    {
        Assert.Throws<ArgumentException>(() =>
            SigningCertificateSelector.BySubjectContains("   "));
    }

    private static FakeInMemorySigningProvider CreateProvider()
    {
        var certificate = new CertificateInfo(
            thumbprint: SampleThumbprint,
            subject: "CN=OpenSignature Test",
            issuer: "CN=OpenSignature Test CA",
            serialNumber: "01",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            providerReference: "fake://cert/1",
            canSign: true,
            publicKeyAlgorithm: "RSA",
            keySizeBits: 2048,
            keyUsages: ["DigitalSignature"],
            enhancedKeyUsages: ["1.3.6.1.5.5.7.3.4"]);

        return new FakeInMemorySigningProvider(
            providerId: "fake-pfx",
            name: "Fake PFX Provider",
            providerType: SigningProviderType.Pfx,
            certificates: [certificate]);
    }
}
