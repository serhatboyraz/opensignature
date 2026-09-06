using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>Decodes JPEG/PNG appearance images into PDF Image XObject payloads.</summary>
internal static class PadesAppearanceImage
{
    public static PdfImageXObject Decode(byte[] imageBytes, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length < 8)
        {
            throw new InvalidOperationException("Appearance image is empty or too small.");
        }

        if (IsJpeg(imageBytes, contentType))
        {
            return DecodeJpeg(imageBytes);
        }

        if (IsPng(imageBytes, contentType))
        {
            return DecodePng(imageBytes);
        }

        throw new InvalidOperationException(
            "Visible signature images must be JPEG or PNG.");
    }

    private static bool IsJpeg(byte[] bytes, string? contentType)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(contentType)
            && (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPng(byte[] bytes, string? contentType)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(signature))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("png", StringComparison.OrdinalIgnoreCase);
    }

    private static PdfImageXObject DecodeJpeg(byte[] jpeg)
    {
        var (width, height, components) = ReadJpegSize(jpeg);
        var colorSpace = components switch
        {
            1 => "/DeviceGray",
            3 => "/DeviceRGB",
            4 => "/DeviceCMYK",
            _ => throw new InvalidOperationException($"Unsupported JPEG component count {components}.")
        };

        return new PdfImageXObject(width, height, colorSpace, "/DCTDecode", jpeg);
    }

    private static (int Width, int Height, int Components) ReadJpegSize(byte[] jpeg)
    {
        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }

            while (i < jpeg.Length && jpeg[i] == 0xFF)
            {
                i++;
            }

            if (i >= jpeg.Length)
            {
                break;
            }

            var marker = jpeg[i++];
            if (marker is 0xD8 or 0xD9 or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (i + 1 >= jpeg.Length)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i));
            if (length < 2 || i + length > jpeg.Length)
            {
                break;
            }

            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (length < 8)
                {
                    break;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 3));
                var width = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 5));
                var components = jpeg[i + 7];
                if (width == 0 || height == 0)
                {
                    throw new InvalidOperationException("JPEG appearance image has invalid dimensions.");
                }

                return (width, height, components);
            }

            i += length;
        }

        throw new InvalidOperationException("JPEG appearance image is missing a Start of Frame marker.");
    }

    private static PdfImageXObject DecodePng(byte[] png)
    {
        var offset = 8;
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        var idat = new MemoryStream();
        var sawHeader = false;

        while (offset + 12 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            offset += 4;
            if (length < 0 || offset + 4 + length + 4 > png.Length)
            {
                throw new InvalidOperationException("PNG appearance image is truncated.");
            }

            var type = Encoding.ASCII.GetString(png, offset, 4);
            offset += 4;
            var data = png.AsSpan(offset, length);
            offset += length + 4;

            if (type == "IHDR")
            {
                if (length < 13)
                {
                    throw new InvalidOperationException("PNG IHDR is invalid.");
                }

                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                bitDepth = data[8];
                colorType = data[9];
                var compression = data[10];
                var filter = data[11];
                var interlace = data[12];
                if (compression != 0 || filter != 0 || interlace != 0)
                {
                    throw new InvalidOperationException(
                        "PNG appearance images must be non-interlaced 8-bit images.");
                }

                sawHeader = true;
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        if (!sawHeader || width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("PNG appearance image is missing IHDR.");
        }

        if (bitDepth != 8 || colorType is not (0 or 2 or 6))
        {
            throw new InvalidOperationException(
                "PNG appearance images must be 8-bit grayscale, RGB, or RGBA.");
        }

        var inflated = InflateZlib(idat.ToArray());
        var rgb = UnfilterPng(inflated, width, height, colorType);
        var compressed = Deflate(rgb);
        return new PdfImageXObject(width, height, "/DeviceRGB", "/FlateDecode", compressed);
    }

    private static byte[] UnfilterPng(byte[] inflated, int width, int height, int colorType)
    {
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            6 => 4,
            _ => throw new InvalidOperationException("Unsupported PNG color type.")
        };

        var stride = width * channels;
        var rowSize = stride + 1;
        if (inflated.Length < rowSize * height)
        {
            throw new InvalidOperationException("PNG pixel data is truncated.");
        }

        var prior = new byte[stride];
        var raw = new byte[stride];
        var rgb = new byte[width * height * 3];
        var dest = 0;

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowSize;
            var filter = inflated[rowStart];
            var src = rowStart + 1;
            for (var x = 0; x < stride; x++)
            {
                var current = inflated[src + x];
                var a = x >= channels ? raw[x - channels] : (byte)0;
                var b = prior[x];
                var c = x >= channels ? prior[x - channels] : (byte)0;
                raw[x] = filter switch
                {
                    0 => current,
                    1 => (byte)(current + a),
                    2 => (byte)(current + b),
                    3 => (byte)(current + ((a + b) / 2)),
                    4 => (byte)(current + Paeth(a, b, c)),
                    _ => throw new InvalidOperationException($"Unsupported PNG filter {filter}.")
                };
            }

            for (var x = 0; x < width; x++)
            {
                if (colorType == 0)
                {
                    var g = raw[x];
                    rgb[dest++] = g;
                    rgb[dest++] = g;
                    rgb[dest++] = g;
                }
                else if (colorType == 2)
                {
                    var i = x * 3;
                    rgb[dest++] = raw[i];
                    rgb[dest++] = raw[i + 1];
                    rgb[dest++] = raw[i + 2];
                }
                else
                {
                    var i = x * 4;
                    var alpha = raw[i + 3];
                    rgb[dest++] = Composite(raw[i], alpha);
                    rgb[dest++] = Composite(raw[i + 1], alpha);
                    rgb[dest++] = Composite(raw[i + 2], alpha);
                }
            }

            Array.Copy(raw, prior, stride);
        }

        return rgb;
    }

    private static byte Composite(byte color, byte alpha) =>
        (byte)((color * alpha + 255 * (255 - alpha) + 127) / 255);

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

    private static byte[] InflateZlib(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}

internal sealed record PdfImageXObject(
    int Width,
    int Height,
    string ColorSpace,
    string Filter,
    byte[] StreamBytes);
