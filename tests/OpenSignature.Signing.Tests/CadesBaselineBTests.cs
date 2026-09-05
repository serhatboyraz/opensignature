using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class CadesBaselineBTests
{
    private const string TestPassword = "unit-test-only-password";

    [Fact]
    public async Task Detached_cades_b_validates_and_tampered_content_fails()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("OpenSignature CAdES detached payload");

        var signer = new CadesBaselineBSigner();
        var result = await signer.SignAsync(content, CadesPackaging.Detached, provider, selector);

        Assert.Equal(CadesPackaging.Detached, result.Packaging);
        CmsSignatureHelper.ValidateSignedCms(result.CmsBytes, content);

        var tampered = Encoding.UTF8.GetBytes("OpenSignature CAdES detached payload!");
        Assert.ThrowsAny<Exception>(() => CmsSignatureHelper.ValidateSignedCms(result.CmsBytes, tampered));
    }

    [Fact]
    public async Task Attached_cades_b_validates_and_exposes_content()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("OpenSignature CAdES attached payload");

        var signer = new CadesBaselineBSigner();
        var result = await signer.SignAsync(content, CadesPackaging.Attached, provider, selector);

        Assert.Equal(CadesPackaging.Attached, result.Packaging);
        CmsSignatureHelper.ValidateSignedCms(result.CmsBytes);
        Assert.Equal(content, CmsSignatureHelper.TryGetEncapsulatedContent(result.CmsBytes));
    }

    private static EphemeralPfx CreateMaterial() =>
        EphemeralPfx.CreateRsa(
            "CN=OpenSignature CAdES",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

    private static PfxSigningProvider CreateProvider(EphemeralPfx material) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-cades",
            Name = "CAdES Test",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

    private static async Task<SigningCertificateSelector> DefaultSelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
