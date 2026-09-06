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

    [Fact]
    public void Structure_reader_keeps_eighteen_pages_when_object_two_is_a_trap()
    {
        var pdf = PdfTestDocuments.CreateEighteenPagePdfWithTrap();

        var structure = PdfStructure.Load(pdf);

        Assert.Equal(PdfTestDocuments.RealCatalogObjectNumber, structure.RootObjectNumber);
        Assert.Equal(PdfTestDocuments.RealPagesObjectNumber, structure.PagesObjectNumber);
        Assert.Equal(PdfTestDocuments.EighteenPageCount, structure.PageCount);
        Assert.Equal(PdfTestDocuments.FirstRealPageObjectNumber, structure.FirstPageObjectNumber);
        Assert.NotEqual(PdfTestDocuments.TrapPagesObjectNumber, structure.PagesObjectNumber);
        Assert.Contains("PAGE-18-MARKER"u8, pdf);
    }

    [Fact]
    public void Structure_reader_uses_last_startxref_not_the_linearization_stub()
    {
        var pdf = PdfTestDocuments.CreateEighteenPagePdfWithTrap();
        var ascii = Encoding.ASCII.GetString(pdf);
        Assert.True(
            ascii.IndexOf("startxref", StringComparison.Ordinal)
            < ascii.LastIndexOf("startxref", StringComparison.Ordinal));

        var structure = PdfStructure.Load(pdf);
        Assert.True(structure.StartXref > 9);
    }

    [Fact]
    public void Structure_reader_resolves_catalog_inside_object_stream()
    {
        var pdf = PdfTestDocuments.CreateXrefStreamPdfWithObjectStreamCatalog();

        var structure = PdfStructure.Load(pdf);

        Assert.Equal(1, structure.RootObjectNumber);
        Assert.Equal(2, structure.PagesObjectNumber);
        Assert.Equal(1, structure.PageCount);
        Assert.Equal(3, structure.FirstPageObjectNumber);
    }

    [Fact]
    public async Task Pades_b_signs_eighteen_page_pdf_and_keeps_all_pages()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var pdf = PdfTestDocuments.CreateEighteenPagePdfWithTrap();

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(pdf, provider, selector);

        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);
        Assert.Contains("PAGE-01-MARKER"u8, result.SignedPdf);
        Assert.Contains("PAGE-18-MARKER"u8, result.SignedPdf);

        var updateAscii = Encoding.ASCII.GetString(result.SignedPdf, pdf.Length, result.SignedPdf.Length - pdf.Length);
        Assert.Contains("/Pages 5 0 R", updateAscii, StringComparison.Ordinal);
        Assert.DoesNotContain("/Pages 2 0 R", updateAscii, StringComparison.Ordinal);

        var signed = PdfStructure.Load(result.SignedPdf);
        Assert.Equal(PdfTestDocuments.EighteenPageCount, signed.PageCount);
        Assert.Equal(PdfTestDocuments.RealPagesObjectNumber, signed.PagesObjectNumber);
        Assert.Equal(PdfTestDocuments.FirstRealPageObjectNumber, signed.FirstPageObjectNumber);
    }

    [Fact]
    public async Task Pades_b_signs_object_stream_pdf()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var pdf = PdfTestDocuments.CreateXrefStreamPdfWithObjectStreamCatalog();

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(pdf, provider, selector);

        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);
        var signed = PdfStructure.Load(result.SignedPdf);
        Assert.Equal(1, signed.PageCount);
        Assert.Equal(2, signed.PagesObjectNumber);
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
