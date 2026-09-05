using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class XadesBaselineBTests
{
    private const string TestPassword = "unit-test-only-password";
    private const string SampleXml = """<Document xmlns="urn:opensignature:test"><Body>Hello XAdES</Body></Document>""";

    [Theory]
    [InlineData(XadesPackaging.Enveloped)]
    [InlineData(XadesPackaging.Enveloping)]
    [InlineData(XadesPackaging.Detached)]
    public async Task Xades_b_packaging_modes_validate(XadesPackaging packaging)
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new XadesBaselineBSigner();
        var result = await signer.SignAsync(Encoding.UTF8.GetBytes(SampleXml), packaging, provider, selector);

        Assert.Equal(packaging, result.Packaging);
        Assert.True(XadesBaselineBSigner.CheckSignature(result.SignedXmlUtf8));
        Assert.Contains("SigningTime", Encoding.UTF8.GetString(result.SignedXmlUtf8), StringComparison.Ordinal);
        Assert.Contains("SigningCertificate", Encoding.UTF8.GetString(result.SignedXmlUtf8), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_enveloped_xml_fails_validation()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new XadesBaselineBSigner();
        var result = await signer.SignAsync(
            Encoding.UTF8.GetBytes(SampleXml),
            XadesPackaging.Enveloped,
            provider,
            selector);

        var xml = Encoding.UTF8.GetString(result.SignedXmlUtf8)
            .Replace("Hello XAdES", "Hello Tampered", StringComparison.Ordinal);
        Assert.False(XadesBaselineBSigner.CheckSignature(Encoding.UTF8.GetBytes(xml)));
    }

    private static EphemeralPfx CreateMaterial() =>
        EphemeralPfx.CreateRsa(
            "CN=OpenSignature XAdES",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

    private static PfxSigningProvider CreateProvider(EphemeralPfx material) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-xades",
            Name = "XAdES Test",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

    private static async Task<SigningCertificateSelector> DefaultSelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
