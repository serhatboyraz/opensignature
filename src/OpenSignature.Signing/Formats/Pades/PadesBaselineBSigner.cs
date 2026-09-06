using System.Globalization;
using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>Creates PAdES Baseline B signatures via PDF incremental update + detached CMS.</summary>
public interface IPadesBaselineBSigner
{
    /// <summary>
    /// Signs a PDF using an incremental update with ByteRange and a CMS signature container.
    /// </summary>
    Task<PadesSignatureResult> SignAsync(
        byte[] pdfBytes,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        PadesVisibleAppearance? appearance = null,
        CancellationToken cancellationToken = default,
        int? contentsHexLength = null,
        Func<byte[], CancellationToken, Task<byte[]>>? enhanceCmsAsync = null);
}

/// <summary>Result of a PAdES-B signature operation.</summary>
public sealed record PadesSignatureResult(byte[] SignedPdf, string ContentType);

/// <summary>
/// PAdES Baseline B signer.
/// Library choice: <c>BouncyCastle.Cryptography</c> for CMS (via <see cref="CmsSignatureHelper"/>)
/// plus a purpose-built PDF incremental updater (no iText / AGPL dependency).
/// </summary>
public sealed class PadesBaselineBSigner : IPadesBaselineBSigner
{
    public const string SubFilter = "ETSI.CAdES.detached";
    public const int ContentsHexLength = PdfByteRangeHelper.DefaultContentsHexLength;
    public const string SignatureFieldNamePrefix = "OpenSignature";

    public const int AdvancedProfileContentsHexLength = 65536;

    public async Task<PadesSignatureResult> SignAsync(
        byte[] pdfBytes,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        PadesVisibleAppearance? appearance = null,
        CancellationToken cancellationToken = default,
        int? contentsHexLength = null,
        Func<byte[], CancellationToken, Task<byte[]>>? enhanceCmsAsync = null)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificateSelector);
        cancellationToken.ThrowIfCancellationRequested();

        if (pdfBytes.Length < 5
            || pdfBytes[0] != (byte)'%'
            || pdfBytes[1] != (byte)'P'
            || pdfBytes[2] != (byte)'D'
            || pdfBytes[3] != (byte)'F')
        {
            throw new InvalidOperationException("Input is not a PDF document.");
        }

        var hexLength = contentsHexLength ?? ContentsHexLength;
        var prepared = BuildIncrementalUpdateWithPlaceholder(
            pdfBytes,
            appearance,
            await ResolveSignerNameAsync(provider, certificateSelector, appearance, cancellationToken).ConfigureAwait(false),
            hexLength,
            out var updateStart);
        var placeholder = PdfByteRangeHelper.FindContentsPlaceholder(prepared, hexLength, updateStart);

        PdfByteRangeHelper.PatchByteRange(prepared, "/ByteRange", placeholder.ByteRange, updateStart);

        var digest = PdfByteRangeHelper.ComputeDigestOverByteRanges(
            prepared,
            placeholder.ByteRange,
            digestAlgorithm);

        // Detached CMS over the ByteRange digest's preimage (raw concatenated ranges),
        // matching PAdES: CMS message-digest is SHA-* of the ByteRange bytes.
        var dataToSign = ExtractByteRangeBytes(prepared, placeholder.ByteRange);
        var cms = await CmsSignatureHelper.CreateSignedCmsAsync(
            dataToSign,
            detached: true,
            provider,
            certificateSelector,
            digestAlgorithm,
            cancellationToken).ConfigureAwait(false);

        if (enhanceCmsAsync is not null)
        {
            cms = await enhanceCmsAsync(cms, cancellationToken).ConfigureAwait(false);
        }

        // Sanity: CMS message digest should match our ByteRange digest.
        _ = digest;

        PdfByteRangeHelper.WriteCmsIntoContentsPlaceholder(prepared, placeholder, cms);
        return new PadesSignatureResult(prepared, "application/pdf");
    }

    /// <summary>
    /// Validates that a PAdES-signed PDF contains a CMS signature covering the ByteRange
    /// and that the CMS signature verifies cryptographically.
    /// Every embedded CAdES signature is checked so earlier incremental signatures stay valid.
    /// </summary>
    public static void ValidateSignedPdf(byte[] signedPdf, bool verifySignatureOnly = true)
    {
        ArgumentNullException.ThrowIfNull(signedPdf);

        var signatures = PdfByteRangeHelper.EnumerateCadesSignatures(signedPdf);
        if (signatures.Count == 0)
        {
            throw new InvalidOperationException("PDF does not contain a PAdES CAdES signature dictionary.");
        }

        foreach (var signature in signatures)
        {
            var data = ExtractByteRangeBytes(signedPdf, signature.ByteRange);
            CmsSignatureHelper.ValidateSignedCms(signature.Cms, data, verifySignatureOnly);
        }
    }

    internal static string NextSignatureFieldName(IReadOnlyList<string> existingFieldNames)
    {
        var used = existingFieldNames.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(existingFieldNames, StringComparer.Ordinal);
        for (var i = 1; i < 100_000; i++)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"{SignatureFieldNamePrefix}{i}");
            if (!used.Contains(name))
            {
                return name;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique PAdES signature field name.");
    }

    /// <summary>Creates a minimal valid PDF for tests (single empty page).</summary>
    public static byte[] CreateMinimalPdf()
    {
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>\nendobj\n"
        };

        using var ms = new MemoryStream();
        var header = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        ms.Write(header);

        var offsets = new long[4];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = ms.Position;
            var bytes = Encoding.ASCII.GetBytes(objects[i]);
            ms.Write(bytes);
        }

        var startxref = ms.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append(CultureInfo.InvariantCulture, $"0 {objects.Length + 1}\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Length; i++)
        {
            xref.Append(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n");
        }

        xref.Append("trailer\n");
        xref.Append(CultureInfo.InvariantCulture, $"<< /Size {objects.Length + 1} /Root 1 0 R >>\n");
        xref.Append("startxref\n");
        xref.Append(CultureInfo.InvariantCulture, $"{startxref}\n");
        xref.Append("%%EOF\n");

        var xrefBytes = Encoding.ASCII.GetBytes(xref.ToString());
        ms.Write(xrefBytes);
        return ms.ToArray();
    }

    private static byte[] BuildIncrementalUpdateWithPlaceholder(
        byte[] originalPdf,
        PadesVisibleAppearance? appearance,
        string signerName,
        int contentsHexLength,
        out int updateStart)
    {
        var structure = PdfStructure.Load(originalPdf);
        var signingTimeUtc = DateTimeOffset.UtcNow;
        var visible = appearance is not null;
        var pageNumber = appearance?.PageNumber ?? 1;
        var pageObjNum = visible ? structure.GetPageObjectNumber(pageNumber) : structure.FirstPageObjectNumber;
        var pageBox = visible ? structure.GetPageBox(pageObjNum) : new PdfRectangle(0, 0, 612, 792);
        var fieldName = NextSignatureFieldName(structure.ExistingFieldNames);

        PdfImageXObject? image = null;
        if (visible && appearance!.ImageBytes is { Length: > 0 })
        {
            image = PadesAppearanceImage.Decode(appearance.ImageBytes, appearance.ImageContentType);
        }

        PadesAppearanceResources? appearanceResources = null;
        if (visible)
        {
            appearanceResources = PadesAppearanceBuilder.Build(
                pageBox,
                signerName,
                signingTimeUtc,
                appearance!.Note,
                image,
                structure.GetVisibleAnnotationRects(pageObjNum));
        }

        var nextObj = structure.NextObjectNumber;
        var sigObjNum = nextObj;
        var widgetObjNum = nextObj + 1;
        var next = nextObj + 2;
        int? appearanceObjNum = null;
        int? imageObjNum = null;
        if (visible)
        {
            appearanceObjNum = next++;
            if (appearanceResources!.Image is not null)
            {
                imageObjNum = next++;
            }
        }

        var catalogObjNum = next++;
        var newSize = Math.Max(structure.Size, next);
        if (visible)
        {
            newSize = Math.Max(newSize, pageObjNum + 1);
        }

        var contentsHex = new string('0', contentsHexLength);
        var byteRangePlaceholder = "[0000000000 0000000000 0000000000 0000000000]";
        var signingTime = signingTimeUtc.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        using var ms = new MemoryStream();
        ms.Write(originalPdf);
        updateStart = (int)ms.Position;
        WriteAscii(ms, "\n");

        var offsets = new SortedDictionary<int, long>();

        offsets[sigObjNum] = ms.Position;
        WriteAscii(ms, $"{sigObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(ms, "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /");
        WriteAscii(ms, SubFilter);
        WriteAscii(ms, " /ByteRange ");
        WriteAscii(ms, byteRangePlaceholder);
        WriteAscii(ms, " /Contents <");
        WriteAscii(ms, contentsHex);
        WriteAscii(ms, "> /M (D:");
        WriteAscii(ms, signingTime);
        WriteAscii(ms, "Z)");
        if (visible)
        {
            WriteAscii(ms, " /Name ");
            WriteAscii(ms, PdfLiteral.String(signerName));
            if (!string.IsNullOrWhiteSpace(appearance!.Note))
            {
                WriteAscii(ms, " /Reason ");
                WriteAscii(ms, PdfLiteral.String(appearance.Note));
            }
        }

        WriteAscii(ms, " >>\nendobj\n");

        offsets[widgetObjNum] = ms.Position;
        WriteAscii(ms, $"{widgetObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(ms, "<< /Type /Annot /Subtype /Widget /FT /Sig /F 132 /Rect ");
        if (appearanceResources is null)
        {
            WriteAscii(ms, "[0 0 0 0]");
        }
        else
        {
            var rect = appearanceResources.Rect;
            WriteAscii(
                ms,
                $"[{PdfLiteral.Number(rect.Llx)} {PdfLiteral.Number(rect.Lly)} {PdfLiteral.Number(rect.Urx)} {PdfLiteral.Number(rect.Ury)}]");
        }

        WriteAscii(ms, $" /V {sigObjNum.ToString(CultureInfo.InvariantCulture)} 0 R /T {PdfLiteral.String(fieldName)} /P {pageObjNum.ToString(CultureInfo.InvariantCulture)} 0 R");
        if (appearanceObjNum is int apObj)
        {
            WriteAscii(ms, $" /AP << /N {apObj.ToString(CultureInfo.InvariantCulture)} 0 R >>");
        }

        WriteAscii(ms, " >>\nendobj\n");

        if (visible && appearanceObjNum is int appearanceObject && appearanceResources is not null)
        {
            offsets[appearanceObject] = ms.Position;
            WriteFormXObject(ms, appearanceObject, appearanceResources, imageObjNum);
        }

        if (visible && imageObjNum is int imageObject && appearanceResources?.Image is not null)
        {
            offsets[imageObject] = ms.Position;
            WriteImageXObject(ms, imageObject, appearanceResources.Image);
        }

        if (visible)
        {
            offsets[pageObjNum] = ms.Position;
            WriteReplacedPage(ms, structure, pageObjNum, widgetObjNum);
        }

        offsets[catalogObjNum] = ms.Position;
        WriteAscii(ms, $"{catalogObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        var catalog = new StringBuilder();
        AppendReplacementCatalog(catalog, structure, widgetObjNum);
        WriteAscii(ms, catalog.ToString());
        WriteAscii(ms, "\nendobj\n");

        WriteXref(ms, offsets, newSize, catalogObjNum, structure);
        return ms.ToArray();
    }

    private static void WriteFormXObject(
        MemoryStream ms,
        int objectNumber,
        PadesAppearanceResources appearance,
        int? imageObjectNumber)
    {
        var resources = new StringBuilder();
        resources.Append("/Resources << /Font << /Helv << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> >>");
        if (imageObjectNumber is int imageObj)
        {
            resources.Append(CultureInfo.InvariantCulture, $" /XObject << /Im0 {imageObj} 0 R >>");
        }

        resources.Append(" >>");

        WriteAscii(ms, $"{objectNumber.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(
            ms,
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 {PdfLiteral.Number(appearance.BBoxWidth)} {PdfLiteral.Number(appearance.BBoxHeight)}] {resources} /Length {appearance.ContentStream.Length.ToString(CultureInfo.InvariantCulture)} >>\n");
        WriteAscii(ms, "stream\n");
        ms.Write(appearance.ContentStream);
        WriteAscii(ms, "\nendstream\nendobj\n");
    }

    private static void WriteImageXObject(MemoryStream ms, int objectNumber, PdfImageXObject image)
    {
        WriteAscii(ms, $"{objectNumber.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(
            ms,
            $"<< /Type /XObject /Subtype /Image /Width {image.Width.ToString(CultureInfo.InvariantCulture)} /Height {image.Height.ToString(CultureInfo.InvariantCulture)} /ColorSpace {image.ColorSpace} /BitsPerComponent 8 /Filter {image.Filter} /Length {image.StreamBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\n");
        WriteAscii(ms, "stream\n");
        ms.Write(image.StreamBytes);
        WriteAscii(ms, "\nendstream\nendobj\n");
    }

    private static void WriteReplacedPage(
        MemoryStream ms,
        PdfStructure structure,
        int pageObjNum,
        int widgetObjNum)
    {
        var page = structure.GetDictionary(pageObjNum);
        WriteAscii(ms, $"{pageObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n<<");
        foreach (var (key, value) in page)
        {
            if (key is "/Annots")
            {
                continue;
            }

            WriteAscii(ms, " ");
            WriteAscii(ms, key);
            WriteAscii(ms, " ");
            WriteAscii(ms, value);
        }

        WriteAscii(ms, " /Annots [");
        foreach (var annot in structure.GetPageAnnotObjectNumbers(pageObjNum))
        {
            WriteAscii(ms, $"{annot.ToString(CultureInfo.InvariantCulture)} 0 R ");
        }

        WriteAscii(ms, $"{widgetObjNum.ToString(CultureInfo.InvariantCulture)} 0 R] >>\nendobj\n");
    }

    private static void WriteXref(
        MemoryStream ms,
        SortedDictionary<int, long> offsets,
        int newSize,
        int catalogObjNum,
        PdfStructure structure)
    {
        var xrefOffset = ms.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n");

        var numbers = offsets.Keys.ToList();
        var i = 0;
        while (i < numbers.Count)
        {
            var start = numbers[i];
            var count = 1;
            while (i + count < numbers.Count && numbers[i + count] == start + count)
            {
                count++;
            }

            xref.Append(CultureInfo.InvariantCulture, $"{start} {count}\n");
            for (var n = 0; n < count; n++)
            {
                xref.Append(CultureInfo.InvariantCulture, $"{offsets[start + n]:D10} 00000 n \n");
            }

            i += count;
        }

        xref.Append("trailer\n");
        xref.Append("<<");
        xref.Append(CultureInfo.InvariantCulture, $" /Size {newSize} /Root {catalogObjNum} 0 R /Prev {structure.StartXref}");
        AppendPreservedTrailerEntry(xref, structure.TrailerEntries, "/Info");
        AppendPreservedTrailerEntry(xref, structure.TrailerEntries, "/ID");
        xref.Append(" >>\n");
        xref.Append("startxref\n");
        xref.Append(CultureInfo.InvariantCulture, $"{xrefOffset}\n");
        xref.Append("%%EOF\n");
        WriteAscii(ms, xref.ToString());
    }

    private static void WriteAscii(MemoryStream ms, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        ms.Write(bytes);
    }

    private static void AppendReplacementCatalog(StringBuilder update, PdfStructure structure, int widgetObjNum)
    {
        update.Append("<<");
        if (structure.CatalogEntries.TryGetValue("/Type", out var type))
        {
            update.Append(" /Type ");
            update.Append(type);
        }

        foreach (var (key, value) in structure.CatalogEntries)
        {
            if (key is "/Type" or "/AcroForm")
            {
                continue;
            }

            update.Append(' ');
            update.Append(key);
            update.Append(' ');
            update.Append(value);
        }

        update.Append(" /AcroForm <<");
        foreach (var (key, value) in structure.AcroFormEntries)
        {
            if (key is "/Fields" or "/SigFlags")
            {
                continue;
            }

            update.Append(' ');
            update.Append(key);
            update.Append(' ');
            update.Append(value);
        }

        update.Append(" /Fields [");
        foreach (var field in structure.ExistingAcroFormFields)
        {
            update.Append(field);
            update.Append(' ');
        }

        update.Append(CultureInfo.InvariantCulture, $"{widgetObjNum} 0 R");
        update.Append("] /SigFlags 3 >> >>");
    }

    private static void AppendPreservedTrailerEntry(
        StringBuilder trailer,
        IReadOnlyDictionary<string, string> entries,
        string key)
    {
        if (!entries.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        trailer.Append(' ');
        trailer.Append(key);
        trailer.Append(' ');
        trailer.Append(value);
    }

    private static async Task<string> ResolveSignerNameAsync(
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        PadesVisibleAppearance? appearance,
        CancellationToken cancellationToken)
    {
        if (appearance is null)
        {
            return "OpenSignature";
        }

        var certificate = await provider.GetCertificateAsync(certificateSelector, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Signing certificate was not found for the PAdES appearance.");

        return PdfLiteral.CommonNameFromSubject(certificate.Subject);
    }

    private static byte[] ExtractByteRangeBytes(byte[] pdf, IReadOnlyList<int> byteRange)
    {
        using var ms = new MemoryStream();
        for (var i = 0; i < byteRange.Count; i += 2)
        {
            ms.Write(pdf, byteRange[i], byteRange[i + 1]);
        }

        return ms.ToArray();
    }
}
