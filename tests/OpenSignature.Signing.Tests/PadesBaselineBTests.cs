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

    [Fact]
    public async Task Invisible_default_uses_zero_rect()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(PadesBaselineBSigner.CreateMinimalPdf(), provider, selector);

        var ascii = Encoding.ASCII.GetString(result.SignedPdf);
        Assert.Contains("/Rect [0 0 0 0]", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Form", ascii, StringComparison.Ordinal);
        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);
    }

    [Fact]
    public async Task Visible_text_appearance_includes_signed_by_and_note()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var pdf = PadesBaselineBSigner.CreateMinimalPdf();

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(
            pdf,
            provider,
            selector,
            appearance: new PadesVisibleAppearance("Approved for release", ImageBytes: null, ImageContentType: null));

        var ascii = Encoding.ASCII.GetString(result.SignedPdf);
        Assert.Contains("Digitally signed by OpenSignature PAdES", ascii, StringComparison.Ordinal);
        Assert.Contains("Approved for release", ascii, StringComparison.Ordinal);
        Assert.Contains("/Subtype /Form", ascii, StringComparison.Ordinal);
        Assert.Contains("/AP << /N", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("/Rect [0 0 0 0]", ascii, StringComparison.Ordinal);
        Assert.Contains("/Annots [", ascii, StringComparison.Ordinal);
        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);

        var signed = PdfStructure.Load(result.SignedPdf);
        Assert.Equal(1, signed.PageCount);
        Assert.Equal(3, signed.FirstPageObjectNumber);
    }

    [Fact]
    public async Task Visible_jpeg_appearance_embeds_image_xobject()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(
            PadesBaselineBSigner.CreateMinimalPdf(),
            provider,
            selector,
            appearance: new PadesVisibleAppearance(
                "With image",
                TinyJpeg(),
                "image/jpeg"));

        var ascii = Encoding.ASCII.GetString(result.SignedPdf);
        Assert.Contains("/Subtype /Image", ascii, StringComparison.Ordinal);
        Assert.Contains("/DCTDecode", ascii, StringComparison.Ordinal);
        Assert.Contains("/Im0", ascii, StringComparison.Ordinal);
        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);
    }

    [Fact]
    public async Task Visible_png_appearance_embeds_flate_image()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        var signer = new PadesBaselineBSigner();
        var result = await signer.SignAsync(
            PadesBaselineBSigner.CreateMinimalPdf(),
            provider,
            selector,
            appearance: new PadesVisibleAppearance(
                null,
                TinyPng(),
                "image/png"));

        var ascii = Encoding.ASCII.GetString(result.SignedPdf);
        Assert.Contains("/Subtype /Image", ascii, StringComparison.Ordinal);
        Assert.Contains("/FlateDecode", ascii, StringComparison.Ordinal);
        Assert.Contains("Digitally signed by OpenSignature PAdES", ascii, StringComparison.Ordinal);
        PadesBaselineBSigner.ValidateSignedPdf(result.SignedPdf);
    }

    [Fact]
    public void Common_name_is_parsed_from_subject()
    {
        Assert.Equal("Alice Example", PdfLiteral.CommonNameFromSubject("CN=Alice Example, O=OpenSignature"));
        Assert.Equal("Unknown signer", PdfLiteral.CommonNameFromSubject(" "));
    }

    private static byte[] TinyJpeg() =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x03, 0x01, 0x11, 0x00,
        0xFF, 0xD9
    ];

    private static byte[] TinyPng()
    {
        var scanline = new byte[] { 0, 200, 30, 40 };
        using var deflateBuffer = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(deflateBuffer, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(scanline);
        }

        var idat = deflateBuffer.ToArray();
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WritePngChunk(png, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        WritePngChunk(png, "IDAT", idat);
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream png, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(data.Length));
        png.Write(length);
        png.Write(typeBytes);
        png.Write(data);
        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        var crc = System.Net.IPAddress.HostToNetworkOrder(unchecked((int)Crc32(crcInput)));
        png.Write(BitConverter.GetBytes(crc));
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                var mask = (crc & 1) != 0 ? 0xEDB88320 : 0U;
                crc = (crc >> 1) ^ mask;
            }
        }

        return ~crc;
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
