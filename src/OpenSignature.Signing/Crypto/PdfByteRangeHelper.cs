using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Crypto;

/// <summary>
/// PDF ByteRange / Contents placeholder helpers for PAdES incremental updates.
/// </summary>
public static partial class PdfByteRangeHelper
{
    /// <summary>Default hex capacity for CMS Contents (8192 hex chars = 4096 bytes).</summary>
    public const int DefaultContentsHexLength = 8192;

    /// <summary>
    /// Computes a digest over the concatenation of PDF ByteRange segments.
    /// <paramref name="byteRange"/> is [offset1, length1, offset2, length2, ...].
    /// </summary>
    public static byte[] ComputeDigestOverByteRanges(
        byte[] pdf,
        IReadOnlyList<int> byteRange,
        DigestAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(byteRange);

        if (byteRange.Count < 2 || byteRange.Count % 2 != 0)
        {
            throw new ArgumentException("ByteRange must contain offset/length pairs.", nameof(byteRange));
        }

        using var hash = algorithm switch
        {
            DigestAlgorithm.Sha256 => (HashAlgorithm)SHA256.Create(),
            DigestAlgorithm.Sha384 => SHA384.Create(),
            DigestAlgorithm.Sha512 => SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };

        for (var i = 0; i < byteRange.Count; i += 2)
        {
            var offset = byteRange[i];
            var length = byteRange[i + 1];
            if (offset < 0 || length < 0 || offset + length > pdf.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(byteRange),
                    $"Invalid ByteRange segment offset={offset}, length={length}, pdfLength={pdf.Length}.");
            }

            hash.TransformBlock(pdf, offset, length, null, 0);
        }

        hash.TransformFinalBlock([], 0, 0);
        return hash.Hash ?? throw new CryptographicException("Digest computation failed.");
    }

    /// <summary>
    /// Locates a hex Contents placeholder and derives a two-segment ByteRange covering the PDF
    /// excluding the Contents value (including angle brackets).
    /// </summary>
    public static PdfContentsPlaceholder FindContentsPlaceholder(
        byte[] pdf,
        int contentsHexLength,
        int searchFromOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (contentsHexLength <= 0 || contentsHexLength % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentsHexLength), "Contents hex length must be a positive even number.");
        }

        if (searchFromOffset < 0 || searchFromOffset > pdf.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(searchFromOffset));
        }

        // Match the last /Contents <hex...> (the incremental signature dictionary).
        var ascii = Encoding.ASCII.GetString(pdf);
        var matches = ContentsPlaceholderRegex().Matches(ascii);
        Match? match = null;
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].Index >= searchFromOffset)
            {
                match = matches[i];
                break;
            }
        }

        if (match is null)
        {
            throw new InvalidOperationException("PDF does not contain a /Contents <...> hex placeholder.");
        }

        var openBracketIndex = match.Groups["hex"].Index - 1; // '<'
        var closeBracketIndex = openBracketIndex + 1 + contentsHexLength; // expected '>'
        if (closeBracketIndex >= pdf.Length || pdf[openBracketIndex] != (byte)'<' || pdf[closeBracketIndex] != (byte)'>')
        {
            // Fall back to actual matched hex length when the constant differs
            var actualHex = match.Groups["hex"].Value;
            openBracketIndex = match.Groups["hex"].Index - 1;
            closeBracketIndex = openBracketIndex + 1 + actualHex.Length;
            contentsHexLength = actualHex.Length;
            if (closeBracketIndex >= pdf.Length || pdf[openBracketIndex] != (byte)'<' || pdf[closeBracketIndex] != (byte)'>')
            {
                throw new InvalidOperationException("Failed to locate Contents hex brackets in PDF bytes.");
            }
        }

        var contentsStart = openBracketIndex; // include '<'
        var contentsEndExclusive = closeBracketIndex + 1; // after '>'
        var byteRange = new[]
        {
            0,
            contentsStart,
            contentsEndExclusive,
            pdf.Length - contentsEndExclusive
        };

        return new PdfContentsPlaceholder(
            ByteRange: byteRange,
            ContentsHexOffset: openBracketIndex + 1,
            ContentsHexLength: contentsHexLength,
            ContentsSpanStart: contentsStart,
            ContentsSpanEndExclusive: contentsEndExclusive);
    }

    /// <summary>
    /// Writes a CMS signature as zero-padded lowercase hex into the Contents placeholder.
    /// </summary>
    public static void WriteCmsIntoContentsPlaceholder(byte[] pdf, PdfContentsPlaceholder placeholder, byte[] cmsDer)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(cmsDer);

        var hex = Convert.ToHexString(cmsDer).ToLowerInvariant();
        if (hex.Length > placeholder.ContentsHexLength)
        {
            throw new InvalidOperationException(
                $"CMS signature hex length {hex.Length} exceeds Contents placeholder capacity {placeholder.ContentsHexLength}.");
        }

        var padded = hex.PadRight(placeholder.ContentsHexLength, '0');
        var hexBytes = Encoding.ASCII.GetBytes(padded);
        Buffer.BlockCopy(hexBytes, 0, pdf, placeholder.ContentsHexOffset, hexBytes.Length);
    }

    /// <summary>
    /// Patches a fixed-width ByteRange array in the PDF (ten-digit zero-padded integers).
    /// </summary>
    public static void PatchByteRange(byte[] pdf, string markerPrefix, IReadOnlyList<int> byteRange)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPrefix);
        ArgumentNullException.ThrowIfNull(byteRange);

        if (byteRange.Count != 4)
        {
            throw new ArgumentException("PAdES Baseline B helper expects a 4-integer ByteRange.", nameof(byteRange));
        }

        var ascii = Encoding.ASCII.GetString(pdf);
        var index = ascii.IndexOf(markerPrefix, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("ByteRange marker was not found in the PDF.");
        }

        var rangeStart = ascii.IndexOf('[', index);
        var rangeEnd = ascii.IndexOf(']', rangeStart + 1);
        if (rangeStart < 0 || rangeEnd < 0)
        {
            throw new InvalidOperationException("ByteRange brackets were not found in the PDF.");
        }

        var formatted = string.Create(
            CultureInfo.InvariantCulture,
            $"[{byteRange[0]:D10} {byteRange[1]:D10} {byteRange[2]:D10} {byteRange[3]:D10}]");

        var existingLength = rangeEnd - rangeStart + 1;
        if (formatted.Length != existingLength)
        {
            throw new InvalidOperationException(
                $"Formatted ByteRange length {formatted.Length} does not match placeholder length {existingLength}.");
        }

        var formattedBytes = Encoding.ASCII.GetBytes(formatted);
        Buffer.BlockCopy(formattedBytes, 0, pdf, rangeStart, formattedBytes.Length);
    }

    /// <summary>Extracts CMS DER bytes from a /Contents &lt;hex&gt; value (trailing '0' padding stripped to even DER).</summary>
    public static byte[] ExtractCmsFromContents(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var ascii = Encoding.ASCII.GetString(pdf);
        var matches = ContentsPlaceholderRegex().Matches(ascii);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("PDF does not contain a /Contents <...> value.");
        }

        var match = matches[^1];

        var hex = match.Groups["hex"].Value;
        // Trim trailing padding zeros conservatively while keeping valid DER
        var cms = Convert.FromHexString(hex);
        return TrimDerPadding(cms);
    }

    private static byte[] TrimDerPadding(byte[] cms)
    {
        // Contents placeholder is zero-padded; DER length is self-describing — parse outer TLV.
        if (cms.Length < 2 || cms[0] != 0x30)
        {
            return cms;
        }

        try
        {
            var offset = 1;
            var length = ReadAsn1Length(cms, ref offset);
            var total = offset + length;
            if (total > 0 && total <= cms.Length)
            {
                return cms.AsSpan(0, total).ToArray();
            }
        }
        catch (InvalidOperationException)
        {
            // Fall through — return as-is.
        }

        return cms;
    }

    private static int ReadAsn1Length(byte[] data, ref int offset)
    {
        if (offset >= data.Length)
        {
            throw new InvalidOperationException("Invalid ASN.1 length.");
        }

        var b = data[offset++];
        if ((b & 0x80) == 0)
        {
            return b;
        }

        var count = b & 0x7F;
        if (count == 0 || count > 4 || offset + count > data.Length)
        {
            throw new InvalidOperationException("Invalid ASN.1 length.");
        }

        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | data[offset++];
        }

        return length;
    }

    [GeneratedRegex(@"\/Contents\s*<(?<hex>[0-9a-fA-F]+)>", RegexOptions.CultureInvariant)]
    private static partial Regex ContentsPlaceholderRegex();
}

/// <summary>Describes a PDF signature Contents placeholder and derived ByteRange.</summary>
public sealed record PdfContentsPlaceholder(
    IReadOnlyList<int> ByteRange,
    int ContentsHexOffset,
    int ContentsHexLength,
    int ContentsSpanStart,
    int ContentsSpanEndExclusive);
