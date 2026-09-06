using System.Globalization;
using System.Text;
using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Profiles;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>
/// Appends PAdES-LT Document Security Store (DSS) and PAdES-LTA document timestamps
/// as PDF incremental updates (does not mutate the original signature ByteRange).
/// </summary>
public sealed class PadesLongTermUpdater
{
    public const string DocTimeStampSubFilter = "ETSI.RFC3161";
    public const int DocumentTimestampContentsHexLength = 16384;

    private readonly ITimestampAuthority _timestampAuthority;

    public PadesLongTermUpdater(ITimestampAuthority timestampAuthority)
    {
        _timestampAuthority = timestampAuthority ?? throw new ArgumentNullException(nameof(timestampAuthority));
    }

    public byte[] AppendDss(byte[] signedPdf, LongTermValidationMaterial material)
    {
        ArgumentNullException.ThrowIfNull(signedPdf);
        ArgumentNullException.ThrowIfNull(material);

        var structure = PdfStructure.Load(signedPdf);
        var next = structure.NextObjectNumber;
        var offsets = new SortedDictionary<int, long>();

        using var ms = new MemoryStream();
        ms.Write(signedPdf);
        WriteAscii(ms, "\n");

        var certRefs = new List<int>();
        foreach (var der in material.CertificatesDer)
        {
            var objNum = next++;
            certRefs.Add(objNum);
            offsets[objNum] = ms.Position;
            WriteStreamObject(ms, objNum, der);
        }

        var crlRefs = new List<int>();
        foreach (var der in material.CrlsDer)
        {
            var objNum = next++;
            crlRefs.Add(objNum);
            offsets[objNum] = ms.Position;
            WriteStreamObject(ms, objNum, der);
        }

        var ocspRefs = new List<int>();
        foreach (var der in material.OcspResponsesDer)
        {
            var objNum = next++;
            ocspRefs.Add(objNum);
            offsets[objNum] = ms.Position;
            WriteStreamObject(ms, objNum, der);
        }

        var dssObjNum = next++;
        offsets[dssObjNum] = ms.Position;
        WriteAscii(ms, $"{dssObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(ms, "<< /Type /DSS");
        WriteRefArray(ms, "/Certs", certRefs);
        WriteRefArray(ms, "/CRLs", crlRefs);
        WriteRefArray(ms, "/OCSPs", ocspRefs);
        WriteAscii(ms, " >>\nendobj\n");

        var catalogObjNum = next++;
        offsets[catalogObjNum] = ms.Position;
        WriteAscii(ms, $"{catalogObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteDssCatalog(ms, structure, dssObjNum);
        WriteAscii(ms, "\nendobj\n");

        WriteXref(ms, offsets, Math.Max(structure.Size, next), catalogObjNum, structure);
        return ms.ToArray();
    }

    public async Task<byte[]> AppendDocumentTimestampAsync(
        byte[] pdf,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        cancellationToken.ThrowIfCancellationRequested();

        var structure = PdfStructure.Load(pdf);
        var tsObjNum = structure.NextObjectNumber;
        var catalogObjNum = tsObjNum + 1;
        var newSize = Math.Max(structure.Size, catalogObjNum + 1);

        var contentsHex = new string('0', DocumentTimestampContentsHexLength);
        var byteRangePlaceholder = "[0000000000 0000000000 0000000000 0000000000]";

        using var ms = new MemoryStream();
        ms.Write(pdf);
        var updateStart = (int)ms.Position;
        WriteAscii(ms, "\n");

        var offsets = new SortedDictionary<int, long>
        {
            [tsObjNum] = ms.Position
        };
        WriteAscii(ms, $"{tsObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(ms, "<< /Type /DocTimeStamp /Filter /Adobe.PPKLite /SubFilter /");
        WriteAscii(ms, DocTimeStampSubFilter);
        WriteAscii(ms, " /ByteRange ");
        WriteAscii(ms, byteRangePlaceholder);
        WriteAscii(ms, " /Contents <");
        WriteAscii(ms, contentsHex);
        WriteAscii(ms, "> >>\nendobj\n");

        offsets[catalogObjNum] = ms.Position;
        WriteAscii(ms, $"{catalogObjNum.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteCatalogCopy(ms, structure);
        WriteAscii(ms, "\nendobj\n");

        WriteXref(ms, offsets, newSize, catalogObjNum, structure);
        var prepared = ms.ToArray();

        var placeholder = PdfByteRangeHelper.FindContentsPlaceholder(
            prepared,
            DocumentTimestampContentsHexLength,
            updateStart);
        PdfByteRangeHelper.PatchByteRange(prepared, "/ByteRange", placeholder.ByteRange, updateStart);

        var data = ExtractByteRangeBytes(prepared, placeholder.ByteRange);
        var imprint = DigestHelper.ComputeDigest(data, digestAlgorithm);
        var token = await _timestampAuthority
            .GetTimestampAsync(imprint, digestAlgorithm, cancellationToken)
            .ConfigureAwait(false);

        PdfByteRangeHelper.WriteCmsIntoContentsPlaceholder(prepared, placeholder, token.Encoded);
        return prepared;
    }

    public static bool HasDss(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var ascii = Encoding.ASCII.GetString(pdf);
        return ascii.Contains("/Type /DSS", StringComparison.Ordinal)
            || ascii.Contains("/DSS ", StringComparison.Ordinal);
    }

    public static bool HasDocumentTimestamp(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var ascii = Encoding.ASCII.GetString(pdf);
        return ascii.Contains("/Type /DocTimeStamp", StringComparison.Ordinal)
            && ascii.Contains("/ETSI.RFC3161", StringComparison.Ordinal);
    }

    private static void WriteStreamObject(MemoryStream ms, int objectNumber, byte[] data)
    {
        WriteAscii(ms, $"{objectNumber.ToString(CultureInfo.InvariantCulture)} 0 obj\n");
        WriteAscii(ms, $"<< /Length {data.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
        ms.Write(data);
        WriteAscii(ms, "\nendstream\nendobj\n");
    }

    private static void WriteRefArray(MemoryStream ms, string key, IReadOnlyList<int> refs)
    {
        if (refs.Count == 0)
        {
            return;
        }

        WriteAscii(ms, " ");
        WriteAscii(ms, key);
        WriteAscii(ms, " [");
        foreach (var n in refs)
        {
            WriteAscii(ms, $"{n.ToString(CultureInfo.InvariantCulture)} 0 R ");
        }

        WriteAscii(ms, "]");
    }

    private static void WriteDssCatalog(MemoryStream ms, PdfStructure structure, int dssObjNum)
    {
        WriteAscii(ms, "<<");
        foreach (var (key, value) in structure.CatalogEntries)
        {
            if (key is "/DSS")
            {
                continue;
            }

            WriteAscii(ms, " ");
            WriteAscii(ms, key);
            WriteAscii(ms, " ");
            WriteAscii(ms, value);
        }

        WriteAscii(ms, $" /DSS {dssObjNum.ToString(CultureInfo.InvariantCulture)} 0 R >>");
    }

    private static void WriteCatalogCopy(MemoryStream ms, PdfStructure structure)
    {
        WriteAscii(ms, "<<");
        foreach (var (key, value) in structure.CatalogEntries)
        {
            WriteAscii(ms, " ");
            WriteAscii(ms, key);
            WriteAscii(ms, " ");
            WriteAscii(ms, value);
        }

        WriteAscii(ms, " >>");
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

        xref.Append("trailer\n<<");
        xref.Append(CultureInfo.InvariantCulture, $" /Size {newSize} /Root {catalogObjNum} 0 R /Prev {structure.StartXref}");
        AppendPreservedTrailerEntry(xref, structure.TrailerEntries, "/Info");
        AppendPreservedTrailerEntry(xref, structure.TrailerEntries, "/ID");
        xref.Append(" >>\nstartxref\n");
        xref.Append(CultureInfo.InvariantCulture, $"{xrefOffset}\n%%EOF\n");
        WriteAscii(ms, xref.ToString());
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

    private static void WriteAscii(MemoryStream ms, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        ms.Write(bytes);
    }

    private static byte[] ExtractByteRangeBytes(byte[] pdf, IReadOnlyList<int> byteRange)
    {
        using var buffer = new MemoryStream();
        for (var i = 0; i < byteRange.Count; i += 2)
        {
            buffer.Write(pdf, byteRange[i], byteRange[i + 1]);
        }

        return buffer.ToArray();
    }
}
