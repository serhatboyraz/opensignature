using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class PadesBaselineBTests
{
    private const string TestPassword = "unit-test-only-password";

    [Fact]
    public async Task Pades_b_signs_minimal_pdf_and_validates()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var pdf = PadesBaselineBSigner.CreateMinimalPdf();

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(pdf, provider, selector);

        Assert.True(result.SignedPdf.Length > pdf.Length);
        Assert.Contains("%PDF"u8, result.SignedPdf.AsSpan(0, 4));
        Assert.Contains("ETSI.CAdES.detached"u8, result.SignedPdf);
        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);
    }

    [Fact]
    public async Task Corrupted_pdf_content_fails_validation()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(PadesBaselineBSigner.CreateMinimalPdf(), provider, selector);

        // Flip a byte in the first ByteRange segment (before Contents).
        result.SignedPdf[20] ^= 0xFF;

        Assert.ThrowsAny<Exception>(() => PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf));
    }

    [Fact]
    public async Task Corrupted_cms_contents_fails_validation()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(PadesBaselineBSigner.CreateMinimalPdf(), provider, selector);

        var ascii = Encoding.ASCII.GetString(result.SignedPdf);
        var contentsIndex = ascii.IndexOf("/Contents <", StringComparison.Ordinal);
        Assert.True(contentsIndex > 0);
        var hexStart = contentsIndex + "/Contents <".Length;
        result.SignedPdf[hexStart] = result.SignedPdf[hexStart] == (byte)'a' ? (byte)'b' : (byte)'a';

        Assert.ThrowsAny<Exception>(() => PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf));
    }

    [Fact]
    public async Task Non_pdf_input_is_rejected()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new PadesBaselineBSigner();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            signer.SignAsync(Encoding.UTF8.GetBytes("not a pdf"), provider, selector));
    }

    private static EphemeralPfx CreateMaterial() =>
        EphemeralPfx.CreateRsa(
            "CN=OpenSignature PAdES",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

    private static PfxSigningProvider CreateProvider(EphemeralPfx material) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-pades",
            Name = "PAdES Test",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

    private static async Task<SigningCertificateSelector> DefaultSelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
