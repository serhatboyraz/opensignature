using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Tests;

public sealed class SigningProviderSelectorTests
{
    [Fact]
    public async Task Resolve_by_type_returns_registered_pfx_provider()
    {
        await using var pfx = CreateFakeProvider("pfx", SigningProviderType.Pfx);
        var selector = new SigningProviderSelector([pfx]);

        var resolved = selector.Resolve(SigningProviderType.Pfx);

        Assert.Same(pfx, resolved);
        Assert.Equal("pfx", resolved.ProviderId);
    }

    [Fact]
    public async Task Resolve_by_id_returns_matching_provider()
    {
        await using var pfx = CreateFakeProvider("pfx-demo", SigningProviderType.Pfx);
        await using var other = CreateFakeProvider("pkcs11-lab", SigningProviderType.Pkcs11);
        var selector = new SigningProviderSelector([pfx, other]);

        var resolved = selector.Resolve(SigningProviderType.Pfx, providerId: "PFX-DEMO");

        Assert.Same(pfx, resolved);
    }

    [Fact]
    public async Task Resolve_rejects_unknown_provider_type()
    {
        await using var pfx = CreateFakeProvider("pfx", SigningProviderType.Pfx);
        var selector = new SigningProviderSelector([pfx]);

        var exception = Assert.Throws<UnsupportedSigningProviderException>(
            () => selector.Resolve(SigningProviderType.Hsm));

        Assert.Equal(SigningProviderType.Hsm, exception.ProviderType);
        Assert.Equal("SIGNING_PROVIDER_UNSUPPORTED", exception.ErrorCode.Value);
        Assert.Contains("Hsm", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_rejects_unknown_provider_id()
    {
        await using var pfx = CreateFakeProvider("pfx", SigningProviderType.Pfx);
        var selector = new SigningProviderSelector([pfx]);

        var exception = Assert.Throws<UnsupportedSigningProviderException>(
            () => selector.Resolve(SigningProviderType.Pfx, providerId: "missing"));

        Assert.Equal("missing", exception.ProviderId);
        Assert.Equal("SIGNING_PROVIDER_UNSUPPORTED", exception.ErrorCode.Value);
    }

    [Fact]
    public async Task Resolve_rejects_provider_id_when_type_mismatches()
    {
        await using var pfx = CreateFakeProvider("pfx", SigningProviderType.Pfx);
        var selector = new SigningProviderSelector([pfx]);

        var exception = Assert.Throws<UnsupportedSigningProviderException>(
            () => selector.Resolve(SigningProviderType.Pkcs11, providerId: "pfx"));

        Assert.Equal(SigningProviderType.Pkcs11, exception.ProviderType);
        Assert.Equal("pfx", exception.ProviderId);
    }

    [Fact]
    public async Task Resolve_by_type_rejects_when_multiple_providers_share_type()
    {
        await using var first = CreateFakeProvider("pfx-a", SigningProviderType.Pfx);
        await using var second = CreateFakeProvider("pfx-b", SigningProviderType.Pfx);
        var selector = new SigningProviderSelector([first, second]);

        var exception = Assert.Throws<UnsupportedSigningProviderException>(
            () => selector.Resolve(SigningProviderType.Pfx));

        Assert.Contains("Specify a provider id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_by_id_selects_among_multiple_providers_of_same_type()
    {
        await using var first = CreateFakeProvider("pfx-a", SigningProviderType.Pfx);
        await using var second = CreateFakeProvider("pfx-b", SigningProviderType.Pfx);
        var selector = new SigningProviderSelector([first, second]);

        var resolved = selector.Resolve(SigningProviderType.Pfx, providerId: "pfx-b");

        Assert.Same(second, resolved);
    }

    [Fact]
    public async Task GetProviders_returns_registered_instances()
    {
        await using var pfx = CreateFakeProvider("pfx", SigningProviderType.Pfx);
        await using var hsm = CreateFakeProvider("hsm-1", SigningProviderType.Hsm);
        var selector = new SigningProviderSelector([pfx, hsm]);

        Assert.Equal(2, selector.GetProviders().Count);
    }

    [Fact]
    public async Task AddPfxSigningProvider_registers_resolver_in_di()
    {
        var services = new ServiceCollection();
        services.AddPfxSigningProvider(options =>
        {
            options.ProviderId = "pfx";
            options.Name = "PFX";
            options.CertificateBytes = CreateMinimalPfx();
            options.Password = "test";
        });

        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ISigningProviderResolver>();
        var signingProviders = provider.GetServices<ISigningProvider>().ToArray();

        Assert.Single(signingProviders);
        var resolved = resolver.Resolve(SigningProviderType.Pfx);
        Assert.Equal("pfx", resolved.ProviderId);
        Assert.Equal(SigningProviderType.Pfx, resolved.ProviderType);
    }

    private static FakeInMemorySigningProvider CreateFakeProvider(
        string providerId,
        SigningProviderType providerType)
    {
        var thumbprint = CertificateThumbprint.Create(new string('b', 40));
        var certificate = new CertificateInfo(
            thumbprint: thumbprint,
            subject: "CN=Selector Test",
            issuer: "CN=Selector Test CA",
            serialNumber: "01",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            providerReference: $"fake://{providerId}",
            canSign: true);

        return new FakeInMemorySigningProvider(
            providerId: providerId,
            name: providerId,
            providerType: providerType,
            certificates: [certificate]);
    }

    private static byte[] CreateMinimalPfx()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=OpenSignature Selector Test",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
        return certificate.Export(
            System.Security.Cryptography.X509Certificates.X509ContentType.Pfx,
            "test");
    }
}
