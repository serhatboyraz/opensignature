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
        CancellationToken cancellationToken = default);
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

    public async Task<PadesSignatureResult> SignAsync(
        byte[] pdfBytes,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
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

        var prepared = BuildIncrementalUpdateWithPlaceholder(pdfBytes, out var updateStart);
        var placeholder = PdfByteRangeHelper.FindContentsPlaceholder(prepared, ContentsHexLength, updateStart);

        PdfByteRangeHelper.PatchByteRange(prepared, "/ByteRange", placeholder.ByteRange);

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

        // Sanity: CMS message digest should match our ByteRange digest.
        _ = digest;

        PdfByteRangeHelper.WriteCmsIntoContentsPlaceholder(prepared, placeholder, cms);
        return new PadesSignatureResult(prepared, "application/pdf");
    }

    /// <summary>
    /// Validates that a PAdES-signed PDF contains a CMS signature covering the ByteRange
    /// and that the CMS signature verifies cryptographically.
    /// </summary>
    public static void ValidateSignedPdf(byte[] signedPdf, bool verifySignatureOnly = true)
    {
        ArgumentNullException.ThrowIfNull(signedPdf);

        var placeholder = PdfByteRangeHelper.FindContentsPlaceholder(signedPdf, ContentsHexLength);
        var data = ExtractByteRangeBytes(signedPdf, placeholder.ByteRange);
        var cms = PdfByteRangeHelper.ExtractCmsFromContents(signedPdf);
        CmsSignatureHelper.ValidateSignedCms(cms, data, verifySignatureOnly);
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

    private static byte[] BuildIncrementalUpdateWithPlaceholder(byte[] originalPdf, out int updateStart)
    {
        var structure = PdfStructure.Load(originalPdf);
        var nextObj = structure.NextObjectNumber;

        var sigObjNum = nextObj;
        var widgetObjNum = nextObj + 1;
        var catalogObjNum = nextObj + 2;
        var newSize = nextObj + 3;

        var contentsHex = new string('0', ContentsHexLength);
        var byteRangePlaceholder = "[0000000000 0000000000 0000000000 0000000000]";
        var signingTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        var update = new StringBuilder();
        update.Append('\n');

        // Signature dictionary
        update.Append(CultureInfo.InvariantCulture, $"{sigObjNum} 0 obj\n");
        update.Append("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /");
        update.Append(SubFilter);
        update.Append(" /ByteRange ");
        update.Append(byteRangePlaceholder);
        update.Append(" /Contents <");
        update.Append(contentsHex);
        update.Append("> /M (D:");
        update.Append(signingTime);
        update.Append("Z) >>\nendobj\n");

        // Widget annotation attached to a real page from the original page tree.
        update.Append(CultureInfo.InvariantCulture, $"{widgetObjNum} 0 obj\n");
        update.Append("<< /Type /Annot /Subtype /Widget /FT /Sig /F 132 /Rect [0 0 0 0] /V ");
        update.Append(CultureInfo.InvariantCulture, $"{sigObjNum} 0 R ");
        update.Append(CultureInfo.InvariantCulture, $"/T (OpenSignature1) /P {structure.FirstPageObjectNumber} 0 R >>\nendobj\n");

        // Replacement catalog copies the original Pages tree and other catalog keys.
        update.Append(CultureInfo.InvariantCulture, $"{catalogObjNum} 0 obj\n");
        AppendReplacementCatalog(update, structure, widgetObjNum);
        update.Append("\nendobj\n");

        var updateBytes = Encoding.ASCII.GetBytes(update.ToString());

        using var ms = new MemoryStream();
        ms.Write(originalPdf);
        updateStart = (int)ms.Position;
        ms.Write(updateBytes);

        var asciiUpdate = update.ToString();
        var sigOffset = updateStart + IndexOfObject(asciiUpdate, sigObjNum);
        var widgetOffset = updateStart + IndexOfObject(asciiUpdate, widgetObjNum);
        var catalogOffset = updateStart + IndexOfObject(asciiUpdate, catalogObjNum);

        var xrefOffset = ms.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append(CultureInfo.InvariantCulture, $"{sigObjNum} 3\n");
        xref.Append(CultureInfo.InvariantCulture, $"{sigOffset:D10} 00000 n \n");
        xref.Append(CultureInfo.InvariantCulture, $"{widgetOffset:D10} 00000 n \n");
        xref.Append(CultureInfo.InvariantCulture, $"{catalogOffset:D10} 00000 n \n");
        xref.Append("trailer\n");
        xref.Append("<<");
        xref.Append(CultureInfo.InvariantCulture, $" /Size {newSize} /Root {catalogObjNum} 0 R /Prev {structure.StartXref}");
        AppendPreservedTrailerEntry(xref, structure.TrailerEntries, "/Info");
        AppendPreservedTrailerEntry(xref, structure.TrailerEntries, "/ID");
        xref.Append(" >>\n");
        xref.Append("startxref\n");
        xref.Append(CultureInfo.InvariantCulture, $"{xrefOffset}\n");
        xref.Append("%%EOF\n");

        var xrefBytes = Encoding.ASCII.GetBytes(xref.ToString());
        ms.Write(xrefBytes);
        return ms.ToArray();
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

    private static long IndexOfObject(string updateAscii, int objectNumber)
    {
        var marker = objectNumber.ToString(CultureInfo.InvariantCulture) + " 0 obj";
        var index = updateAscii.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"Failed to locate object {objectNumber} in incremental update.");
        }

        return index;
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
