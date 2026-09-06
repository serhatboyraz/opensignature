using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Pkcs11;
using OpenSignature.Signing.Pkcs11.Mock;
using OpenSignature.Signing.SmartCard;

namespace OpenSignature.Signing.Tests;

public sealed class SmartCardSigningProviderTests
{
    private const string ModulePath = "mock-smartcard.so";
    private const string PinSecretName = "smartcard-pin";
    private const string Pin = "unit-test-pin";

    [Fact]
    public async Task Mock_token_lists_certificate_signs_digest_and_is_healthy()
    {
        using var library = MockPkcs11Library.CreateWithRsa(
            modulePath: ModulePath,
            expectedPin: Pin,
            subject: "CN=OpenSignature SmartCard Mock");
        await using var provider = CreateProvider(library);

        var certificates = await provider.ListCertificatesAsync();
        var health = await provider.GetHealthAsync();

        Assert.Equal(SigningProviderType.SmartCard, provider.ProviderType);
        Assert.Equal("smartcard-test", provider.ProviderId);
        Assert.Single(certificates);
        Assert.True(certificates[0].CanSign);
        Assert.Equal("RSA", certificates[0].PublicKeyAlgorithm);
        Assert.StartsWith("smartcard://", certificates[0].ProviderReference, StringComparison.Ordinal);
        Assert.True(health.IsHealthy);

        var digest = RandomDigest(DigestAlgorithm.Sha256);
        var signature = await provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByThumbprint(certificates[0].Thumbprint));

        Assert.NotEmpty(signature);
        Assert.True(VerifyRsa(certificates[0], digest, signature));
    }

    [Fact]
    public async Task Invalid_pin_fails_without_echoing_pin()
    {
        using var library = MockPkcs11Library.CreateWithRsa(modulePath: ModulePath, expectedPin: Pin);
        var secrets = new InMemorySigningSecretProvider(new Dictionary<string, string>
        {
            [PinSecretName] = "wrong-pin"
        });
        await using var provider = new SmartCardSigningProvider(
            CreateOptions(),
            new MockPkcs11LibraryFactory(library),
            secrets);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ListCertificatesAsync());
        Assert.DoesNotContain("wrong-pin", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Pin, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_module_reports_unavailable_health()
    {
        using var library = MockPkcs11Library.CreateUnavailable(ModulePath, "Mock module failed to initialize.");
        await using var provider = CreateProvider(library);

        var health = await provider.GetHealthAsync();

        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.False(health.IsHealthy);
        Assert.NotNull(health.Detail);
    }

    [Fact]
    public async Task AddSmartCardSigningProvider_registers_in_resolver()
    {
        using var library = MockPkcs11Library.CreateWithRsa(modulePath: ModulePath, expectedPin: Pin);
        var services = new ServiceCollection();
        services.AddSingleton<IPkcs11LibraryFactory>(new MockPkcs11LibraryFactory(library));
        services.AddSmartCardSigningProvider(
            options =>
            {
                options.ProviderId = "smartcard-di";
                options.Name = "Smart Card DI";
                options.ModulePath = ModulePath;
                options.PinSecretName = PinSecretName;
            },
            secrets: new Dictionary<string, string> { [PinSecretName] = Pin });

        await using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<ISigningProviderResolver>();
        var providers = sp.GetServices<ISigningProvider>().ToArray();

        Assert.Contains(providers, p => p.ProviderType == SigningProviderType.SmartCard);
        var resolved = resolver.Resolve(SigningProviderType.SmartCard);
        Assert.Equal("smartcard-di", resolved.ProviderId);
    }

    [Fact]
    public async Task Session_lifecycle_allows_repeated_operations()
    {
        using var library = MockPkcs11Library.CreateWithRsa(modulePath: ModulePath, expectedPin: Pin);
        await using var provider = CreateProvider(library);

        var certificates = await provider.ListCertificatesAsync();
        var digest = RandomDigest(DigestAlgorithm.Sha256);
        var selector = SigningCertificateSelector.ByThumbprint(certificates[0].Thumbprint);

        var first = await provider.SignDigestAsync(digest, DigestAlgorithm.Sha256, selector);
        var second = await provider.SignDigestAsync(digest, DigestAlgorithm.Sha256, selector);

        Assert.Equal(first, second);
    }

    private static SmartCardSigningProvider CreateProvider(MockPkcs11Library library)
    {
        var secrets = new InMemorySigningSecretProvider(new Dictionary<string, string>
        {
            [PinSecretName] = Pin
        });

        return new SmartCardSigningProvider(
            CreateOptions(),
            new MockPkcs11LibraryFactory(library),
            secrets);
    }

    private static SmartCardSigningProviderOptions CreateOptions() =>
        new()
        {
            ProviderId = "smartcard-test",
            Name = "Smart Card Test",
            ModulePath = ModulePath,
            SlotId = 0,
            PinSecretName = PinSecretName
        };

    private static byte[] RandomDigest(DigestAlgorithm algorithm)
    {
        var bytes = new byte[algorithm.GetDigestLengthBytes()];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static bool VerifyRsa(CertificateInfo certificate, byte[] digest, byte[] signature)
    {
        using var publicCert = X509CertificateLoader.LoadCertificate(certificate.PublicCertificateDer.ToArray());
        using var rsa = publicCert.GetRSAPublicKey();
        Assert.NotNull(rsa);
        return rsa!.VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
