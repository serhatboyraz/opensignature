using System.IO.Compression;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>Decodes PDF stream filters used by xref and object streams.</summary>
internal static class PdfFilters
{
    public static byte[] Decode(byte[] encoded, IReadOnlyDictionary<string, string> dictionary)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(dictionary);

        var filters = ReadNameList(dictionary, "/Filter");
        if (filters.Count == 0)
        {
            return encoded;
        }

        var decodeParms = ReadDecodeParms(dictionary, filters.Count);
        var data = encoded;
        for (var i = 0; i < filters.Count; i++)
        {
            data = DecodeOne(data, filters[i], decodeParms[i]);
        }

        return data;
    }

    private static byte[] DecodeOne(byte[] data, string filter, IReadOnlyDictionary<string, string>? decodeParms)
    {
        if (filter is "/FlateDecode" or "/Fl")
        {
            var inflated = Inflate(data);
            return ApplyPredictor(inflated, decodeParms);
        }

        if (filter is "/ASCIIHexDecode" or "/AHx")
        {
            return DecodeAsciiHex(data);
        }

        throw new InvalidOperationException($"Unsupported PDF stream filter '{filter}'.");
    }

    private static byte[] Inflate(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            using var input = new MemoryStream(data, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
    }

    private static byte[] ApplyPredictor(byte[] data, IReadOnlyDictionary<string, string>? decodeParms)
    {
        if (decodeParms is null || !TryReadInt(decodeParms, "/Predictor", out var predictor) || predictor <= 1)
        {
            return data;
        }

        var columns = TryReadInt(decodeParms, "/Columns", out var col) ? col : 1;
        var colors = TryReadInt(decodeParms, "/Colors", out var c) ? c : 1;
        var bits = TryReadInt(decodeParms, "/BitsPerComponent", out var bpc) ? bpc : 8;
        var bytesPerPixel = Math.Max(1, (colors * bits + 7) / 8);
        var rowLength = columns * bytesPerPixel;

        if (predictor == 2)
        {
            return ApplyTiffPredictor(data, rowLength, bytesPerPixel);
        }

        if (predictor >= 10)
        {
            return ApplyPngPredictor(data, rowLength, bytesPerPixel);
        }

        throw new InvalidOperationException($"Unsupported PDF predictor '{predictor}'.");
    }

    private static byte[] ApplyTiffPredictor(byte[] data, int rowLength, int bytesPerPixel)
    {
        if (rowLength <= 0)
        {
            return data;
        }

        var output = new byte[data.Length];
        Buffer.BlockCopy(data, 0, output, 0, data.Length);
        var rows = output.Length / rowLength;
        for (var row = 0; row < rows; row++)
        {
            var offset = row * rowLength;
            for (var i = bytesPerPixel; i < rowLength; i++)
            {
                output[offset + i] += output[offset + i - bytesPerPixel];
            }
        }

        return output;
    }

    private static byte[] ApplyPngPredictor(byte[] data, int rowLength, int bytesPerPixel)
    {
        if (rowLength <= 0)
        {
            return data;
        }

        var stride = rowLength + 1;
        var rows = data.Length / stride;
        if (rows == 0)
        {
            return data;
        }

        var output = new byte[rows * rowLength];
        var previous = new byte[rowLength];

        for (var row = 0; row < rows; row++)
        {
            var filter = data[row * stride];
            var src = row * stride + 1;
            var dest = row * rowLength;
            for (var i = 0; i < rowLength; i++)
            {
                var raw = data[src + i];
                var left = i >= bytesPerPixel ? output[dest + i - bytesPerPixel] : (byte)0;
                var up = previous[i];
                var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : (byte)0;
                output[dest + i] = filter switch
                {
                    0 => raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    3 => (byte)(raw + ((left + up) / 2)),
                    4 => (byte)(raw + Paeth(left, up, upLeft)),
                    _ => throw new InvalidOperationException($"Unsupported PNG filter '{filter}'.")
                };
            }

            Buffer.BlockCopy(output, dest, previous, 0, rowLength);
        }

        return output;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static byte[] DecodeAsciiHex(byte[] data)
    {
        var hex = new List<char>(data.Length);
        foreach (var b in data)
        {
            if (b == (byte)'>')
            {
                break;
            }

            if (PdfInput.IsWhitespace(b))
            {
                continue;
            }

            hex.Add((char)b);
        }

        if (hex.Count % 2 == 1)
        {
            hex.Add('0');
        }

        return Convert.FromHexString(new string(hex.ToArray()));
    }

    private static List<string> ReadNameList(IReadOnlyDictionary<string, string> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        raw = raw.Trim();
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var input = new PdfInput(System.Text.Encoding.Latin1.GetBytes(raw));
            return input.ReadArrayItems().Select(static item => item.Trim()).ToList();
        }

        return [raw];
    }

    private static IReadOnlyDictionary<string, string>?[] ReadDecodeParms(
        IReadOnlyDictionary<string, string> dictionary,
        int filterCount)
    {
        var result = new IReadOnlyDictionary<string, string>?[filterCount];
        if (!dictionary.TryGetValue("/DecodeParms", out var raw)
            && !dictionary.TryGetValue("/DP", out raw))
        {
            return result;
        }

        raw = raw.Trim();
        if (raw.StartsWith("<<", StringComparison.Ordinal))
        {
            result[0] = new PdfInput(System.Text.Encoding.Latin1.GetBytes(raw)).ReadDictionary();
            return result;
        }

        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var items = new PdfInput(System.Text.Encoding.Latin1.GetBytes(raw)).ReadArrayItems();
            for (var i = 0; i < filterCount && i < items.Count; i++)
            {
                var item = items[i].Trim();
                if (item.StartsWith("<<", StringComparison.Ordinal))
                {
                    result[i] = new PdfInput(System.Text.Encoding.Latin1.GetBytes(item)).ReadDictionary();
                }
            }
        }

        return result;
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> dictionary, string key, out int value)
    {
        value = 0;
        if (!dictionary.TryGetValue(key, out var raw))
        {
            return false;
        }

        var input = new PdfInput(System.Text.Encoding.Latin1.GetBytes(raw));
        if (!input.TryReadNumber(out var number) || number is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)number;
        return true;
    }
}
