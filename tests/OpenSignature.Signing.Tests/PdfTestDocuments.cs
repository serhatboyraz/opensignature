using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OpenSignature.Signing.Tests;

/// <summary>Builds classic, linearized-style, xref-stream, and object-stream PDFs for PAdES tests.</summary>
internal static class PdfTestDocuments
{
    public const int TrapPagesObjectNumber = 2;
    public const int RealCatalogObjectNumber = 4;
    public const int RealPagesObjectNumber = 5;
    public const int FirstRealPageObjectNumber = 6;
    public const int EighteenPageCount = 18;

    public static byte[] CreateEighteenPagePdfWithTrap()
    {
        var objects = new Dictionary<int, string>
        {
            [1] = "<< /Title (OpenSignature trap info) >>",
            [TrapPagesObjectNumber] = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            [3] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            [RealCatalogObjectNumber] = "<< /Type /Catalog /Pages 5 0 R /Lang (en) >>",
            [RealPagesObjectNumber] = BuildKidsPagesDictionary(FirstRealPageObjectNumber, EighteenPageCount)
        };

        for (var i = 0; i < EighteenPageCount; i++)
        {
            var pageObj = FirstRealPageObjectNumber + i;
            var contentsObj = 24 + i;
            var marker = string.Create(CultureInfo.InvariantCulture, $"PAGE-{(i + 1):00}-MARKER");
            var content = Encoding.ASCII.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"BT /F1 12 Tf 72 720 Td ({marker}) Tj ET\n"));
            objects[pageObj] =
                $"<< /Type /Page /Parent 5 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> " +
                $"/Contents {contentsObj} 0 R >>";
            objects[contentsObj] =
                $"<< /Length {content.Length} >>\nstream\n{Encoding.ASCII.GetString(content)}endstream";
        }

        return BuildClassic(objects, RealCatalogObjectNumber, prependDummyStartXref: true);
    }

    public static byte[] CreateXrefStreamPdfWithObjectStreamCatalog()
    {
        using var ms = new MemoryStream();
        ms.Write("%PDF-1.7\n"u8);

        var page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>";
        var pageOffset = (int)ms.Position;
        WriteObject(ms, 3, page);

        var catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        var pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        var header = string.Create(CultureInfo.InvariantCulture, $"1 0 2 {catalog.Length} ");
        var objStmDecoded = Encoding.ASCII.GetBytes(header + catalog + pages);
        var objStmCompressed = ZlibCompress(objStmDecoded);
        var objStmBody =
            $"<< /Type /ObjStm /N 2 /First {header.Length} /Filter /FlateDecode /Length {objStmCompressed.Length} >>\n" +
            "stream\n";
        var objStmOffset = (int)ms.Position;
        WriteObject(ms, 5, objStmBody, objStmCompressed, "\nendstream");

        var xrefOffset = (int)ms.Position;
        var widths = new[] { 1, 2, 2 };
        var entrySize = widths.Sum();
        var xrefUncompressed = new byte[7 * entrySize];
        WriteXrefEntry(xrefUncompressed, 0, widths, type: 0, field2: 0, field3: 65535); // 0 free
        WriteXrefEntry(xrefUncompressed, 1, widths, type: 2, field2: 5, field3: 0); // catalog in ObjStm
        WriteXrefEntry(xrefUncompressed, 2, widths, type: 2, field2: 5, field3: 1); // pages in ObjStm
        WriteXrefEntry(xrefUncompressed, 3, widths, type: 1, field2: pageOffset, field3: 0);
        WriteXrefEntry(xrefUncompressed, 4, widths, type: 0, field2: 0, field3: 0);
        WriteXrefEntry(xrefUncompressed, 5, widths, type: 1, field2: objStmOffset, field3: 0);
        WriteXrefEntry(xrefUncompressed, 6, widths, type: 1, field2: xrefOffset, field3: 0);

        var predicted = PngUpEncode(xrefUncompressed, entrySize);
        var xrefCompressed = ZlibCompress(predicted);
        var xrefBody =
            $"<< /Type /XRef /Size 7 /Root 1 0 R /W [1 2 2] /Index [0 7] " +
            $"/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns {entrySize} >> " +
            $"/Length {xrefCompressed.Length} >>\nstream\n";
        WriteObject(ms, 6, xrefBody, xrefCompressed, "\nendstream");

        var startxref = Encoding.ASCII.GetBytes($"startxref\n{xrefOffset}\n%%EOF\n");
        ms.Write(startxref);
        return ms.ToArray();
    }

    private static string BuildKidsPagesDictionary(int firstPageObject, int pageCount)
    {
        var kids = new StringBuilder();
        kids.Append("<< /Type /Pages /Kids [");
        for (var i = 0; i < pageCount; i++)
        {
            if (i > 0)
            {
                kids.Append(' ');
            }

            kids.Append(CultureInfo.InvariantCulture, $"{firstPageObject + i} 0 R");
        }

        kids.Append(CultureInfo.InvariantCulture, $"] /Count {pageCount} >>");
        return kids.ToString();
    }

    private static byte[] BuildClassic(IReadOnlyDictionary<int, string> objects, int root, bool prependDummyStartXref)
    {
        using var ms = new MemoryStream();
        ms.Write("%PDF-1.7\n"u8);
        if (prependDummyStartXref)
        {
            ms.Write("startxref\n9\n%%EOF\n"u8);
        }

        var size = objects.Keys.Max() + 1;
        var offsets = new int[size];
        for (var i = 1; i < size; i++)
        {
            if (!objects.TryGetValue(i, out var body))
            {
                continue;
            }

            offsets[i] = (int)ms.Position;
            WriteObject(ms, i, body);
        }

        var startxref = (int)ms.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append(CultureInfo.InvariantCulture, $"0 {size}\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i < size; i++)
        {
            if (!objects.ContainsKey(i))
            {
                xref.Append("0000000000 00000 f \n");
                continue;
            }

            xref.Append(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n");
        }

        xref.Append("trailer\n");
        xref.Append(CultureInfo.InvariantCulture, $"<< /Size {size} /Root {root} 0 R >>\n");
        xref.Append("startxref\n");
        xref.Append(CultureInfo.InvariantCulture, $"{startxref}\n");
        xref.Append("%%EOF\n");
        ms.Write(Encoding.ASCII.GetBytes(xref.ToString()));
        return ms.ToArray();
    }

    private static void WriteObject(MemoryStream ms, int objectNumber, string body, byte[]? stream = null, string? suffix = null)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{objectNumber} 0 obj\n"));
        ms.Write(header);
        ms.Write(Encoding.ASCII.GetBytes(body));
        if (stream is not null)
        {
            ms.Write(stream);
        }

        if (suffix is not null)
        {
            ms.Write(Encoding.ASCII.GetBytes(suffix));
        }

        if (stream is null && !body.EndsWith('\n'))
        {
            ms.Write("\n"u8);
        }

        ms.Write("endobj\n"u8);
    }

    private static void WriteXrefEntry(byte[] data, int objectNumber, int[] widths, int type, int field2, int field3)
    {
        var offset = objectNumber * widths.Sum();
        WritePacked(data, offset, widths[0], type);
        WritePacked(data, offset + widths[0], widths[1], field2);
        WritePacked(data, offset + widths[0] + widths[1], widths[2], field3);
    }

    private static void WritePacked(byte[] data, int offset, int width, int value)
    {
        for (var i = 0; i < width; i++)
        {
            data[offset + i] = (byte)(value >> (8 * (width - 1 - i)));
        }
    }

    private static byte[] PngUpEncode(byte[] data, int columns)
    {
        var rows = data.Length / columns;
        var output = new byte[rows * (columns + 1)];
        var previous = new byte[columns];
        for (var row = 0; row < rows; row++)
        {
            output[row * (columns + 1)] = 2;
            for (var i = 0; i < columns; i++)
            {
                var current = data[(row * columns) + i];
                output[(row * (columns + 1)) + 1 + i] = (byte)(current - previous[i]);
                previous[i] = current;
            }
        }

        return output;
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
