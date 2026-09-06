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
    /// Reads the last <c>/ByteRange [a b c d]</c> array stored in the PDF (the values written at signing time).
    /// After later incremental updates, this must be used instead of recomputing ranges from the current file length.
    /// </summary>
    public static int[] ReadStoredByteRange(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var signatures = EnumerateCadesSignatures(pdf);
        if (signatures.Count == 0)
        {
            throw new InvalidOperationException("PDF does not contain a /ByteRange array.");
        }

        return signatures[^1].ByteRange.ToArray();
    }

    /// <summary>
    /// Enumerates every PAdES CAdES signature dictionary in document order (incremental updates last).
    /// Document timestamps (<c>/ETSI.RFC3161</c>) are not included.
    /// </summary>
    public static IReadOnlyList<PadesEmbeddedCadesSignature> EnumerateCadesSignatures(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var ascii = Encoding.ASCII.GetString(pdf);
        const string marker = "/SubFilter /ETSI.CAdES.detached";
        var results = new List<PadesEmbeddedCadesSignature>();
        var searchFrom = 0;
        while (true)
        {
            var sub = ascii.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (sub < 0)
            {
                break;
            }

            searchFrom = sub + marker.Length;
            var dictEnd = ascii.IndexOf(">>", sub, StringComparison.Ordinal);
            if (dictEnd < 0)
            {
                throw new InvalidOperationException("PAdES signature dictionary is unterminated.");
            }

            var byteRange = ParseByteRange(ascii, sub, dictEnd);
            var cms = ParseContentsCms(ascii, sub, dictEnd);
            results.Add(new PadesEmbeddedCadesSignature(byteRange, cms));
        }

        return results;
    }

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
    public static void PatchByteRange(
        byte[] pdf,
        string markerPrefix,
        IReadOnlyList<int> byteRange,
        int searchFromOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPrefix);
        ArgumentNullException.ThrowIfNull(byteRange);

        if (byteRange.Count != 4)
        {
            throw new ArgumentException("PAdES Baseline B helper expects a 4-integer ByteRange.", nameof(byteRange));
        }

        var ascii = Encoding.ASCII.GetString(pdf);
        var index = ascii.IndexOf(markerPrefix, searchFromOffset, StringComparison.Ordinal);
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
        var signatures = EnumerateCadesSignatures(pdf);
        if (signatures.Count == 0)
        {
            throw new InvalidOperationException("PDF does not contain a /Contents <...> value.");
        }

        return signatures[^1].Cms;
    }

    private static int[] ParseByteRange(string ascii, int fromInclusive, int toExclusive)
    {
        var index = ascii.IndexOf("/ByteRange", fromInclusive, StringComparison.Ordinal);
        if (index < 0 || index >= toExclusive)
        {
            throw new InvalidOperationException("PDF signature dictionary is missing /ByteRange.");
        }

        var rangeStart = ascii.IndexOf('[', index);
        var rangeEnd = ascii.IndexOf(']', rangeStart + 1);
        if (rangeStart < 0 || rangeEnd < 0 || rangeEnd > toExclusive)
        {
            throw new InvalidOperationException("PDF /ByteRange brackets were not found.");
        }

        var inner = ascii.Substring(rangeStart + 1, rangeEnd - rangeStart - 1)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (inner.Length != 4)
        {
            throw new InvalidOperationException("PDF /ByteRange must contain four integers.");
        }

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(inner[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                throw new InvalidOperationException($"PDF /ByteRange value '{inner[i]}' is not an integer.");
            }
        }

        return values;
    }

    private static byte[] ParseContentsCms(string ascii, int fromInclusive, int toExclusive)
    {
        var match = ContentsPlaceholderRegex().Match(ascii, fromInclusive);
        if (!match.Success || match.Index >= toExclusive)
        {
            throw new InvalidOperationException("PDF signature dictionary is missing /Contents <...>.");
        }

        var cms = Convert.FromHexString(match.Groups["hex"].Value);
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

/// <summary>One embedded PAdES CAdES signature (ByteRange + CMS DER).</summary>
public sealed record PadesEmbeddedCadesSignature(IReadOnlyList<int> ByteRange, byte[] Cms);
