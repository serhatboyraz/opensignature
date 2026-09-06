using System.Globalization;
using System.Text;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>
/// Reads PDF cross-reference tables/streams (including object streams) so incremental
/// PAdES updates can preserve the original catalog Pages tree.
/// </summary>
internal sealed class PdfStructure
{
    private readonly byte[] _pdf;
    private readonly Dictionary<int, PdfXrefEntry> _xref = [];
    private readonly Dictionary<int, Dictionary<string, string>> _dictionaryCache = [];
    private readonly Dictionary<int, byte[]> _objectStreamCache = [];
    private Dictionary<(int ObjectNumber, int Generation), List<int>>? _scannedObjectHeaders;

    private PdfStructure(byte[] pdf)
    {
        _pdf = pdf;
    }

    public int Size { get; private set; }

    public int RootObjectNumber { get; private set; }

    public int PagesObjectNumber { get; private set; }

    public int FirstPageObjectNumber { get; private set; }

    public int PageCount { get; private set; }

    public int StartXref { get; private set; }

    public int NextObjectNumber { get; private set; }

    public bool IsEncrypted { get; private set; }

    public IReadOnlyDictionary<string, string> CatalogEntries { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> TrailerEntries { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> ExistingAcroFormFields { get; private set; } = [];

    public IReadOnlyDictionary<string, string> AcroFormEntries { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>1-based page index; throws when the page does not exist.</summary>
    public int GetPageObjectNumber(int pageNumber)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page number must be at least 1.");
        }

        var pages = FlattenPageObjectNumbers();
        if (pageNumber > pages.Count)
        {
            throw new InvalidOperationException(
                $"PDF has {pages.Count} page(s); appearance page {pageNumber} is out of range.");
        }

        return pages[pageNumber - 1];
    }

    /// <summary>Visible page box (CropBox, else MediaBox, else inherited from the page tree).</summary>
    public PdfRectangle GetPageBox(int pageObjectNumber)
    {
        var current = pageObjectNumber;
        var visited = new HashSet<int>();
        while (visited.Add(current))
        {
            var dict = GetDictionary(current);
            if (TryReadRectangle(dict, "/CropBox", out var crop))
            {
                return crop;
            }

            if (TryReadRectangle(dict, "/MediaBox", out var media))
            {
                return media;
            }

            if (!dict.TryGetValue("/Parent", out var parentRaw)
                || !PdfInput.TryParseReference(parentRaw, out var parent, out _))
            {
                break;
            }

            current = parent;
        }

        return new PdfRectangle(0, 0, 612, 792);
    }

    /// <summary>Existing page annotation object numbers (may be empty).</summary>
    public IReadOnlyList<int> GetPageAnnotObjectNumbers(int pageObjectNumber)
    {
        var dict = GetDictionary(pageObjectNumber);
        return ResolveReferenceArray(dict, "/Annots");
    }

    public static PdfStructure Load(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.Length < 5
            || pdf[0] != (byte)'%'
            || pdf[1] != (byte)'P'
            || pdf[2] != (byte)'D'
            || pdf[3] != (byte)'F')
        {
            throw new InvalidOperationException("Input is not a PDF document.");
        }

        var structure = new PdfStructure(pdf);
        structure.LoadXref();
        if (structure.IsEncrypted)
        {
            throw new InvalidOperationException("Encrypted PDFs are not supported for PAdES signing.");
        }

        structure.LoadCatalog();
        return structure;
    }

    public Dictionary<string, string> GetDictionary(int objectNumber)
    {
        if (_dictionaryCache.TryGetValue(objectNumber, out var cached))
        {
            return cached;
        }

        var value = ReadObjectValue(objectNumber);
        var dictionary = value.Dictionary
            ?? throw new InvalidOperationException($"PDF object {objectNumber} is not a dictionary.");
        _dictionaryCache[objectNumber] = dictionary;
        return dictionary;
    }

    private void LoadXref()
    {
        StartXref = FindLastStartXref(_pdf);
        var visited = new HashSet<int>();
        var current = StartXref;
        Dictionary<string, string>? newestTrailer = null;

        while (visited.Add(current))
        {
            var (entries, trailer, prev) = ReadXrefSection(current);
            newestTrailer ??= trailer;
            foreach (var entry in entries)
            {
                _xref.TryAdd(entry.Key, entry.Value);
            }

            if (prev is null)
            {
                break;
            }

            current = prev.Value;
        }

        TrailerEntries = newestTrailer
            ?? throw new InvalidOperationException("PDF trailer dictionary was not found.");

        if (!TryReadInt(TrailerEntries, "/Size", out var size) || size < 1)
        {
            throw new InvalidOperationException("PDF trailer is missing /Size.");
        }

        Size = size;
        if (!TrailerEntries.TryGetValue("/Root", out var rootRaw)
            || !PdfInput.TryParseReference(rootRaw, out var root, out _))
        {
            throw new InvalidOperationException("PDF trailer is missing /Root.");
        }

        RootObjectNumber = root;
        IsEncrypted = TrailerEntries.ContainsKey("/Encrypt");

        var maxObject = _xref.Count == 0 ? 0 : _xref.Keys.Max();
        NextObjectNumber = Math.Max(Size, maxObject + 1);
    }

    private void LoadCatalog()
    {
        CatalogEntries = GetDictionary(RootObjectNumber);
        if (!CatalogEntries.TryGetValue("/Pages", out var pagesRaw)
            || !PdfInput.TryParseReference(pagesRaw, out var pages, out _))
        {
            throw new InvalidOperationException(
                $"PDF catalog object {RootObjectNumber} is missing a /Pages reference.");
        }

        PagesObjectNumber = pages;
        var flattened = FlattenPageObjectNumbers();
        PageCount = flattened.Count > 0
            ? flattened.Count
            : TryReadInt(GetDictionary(pages), "/Count", out var count) ? count : 1;
        FirstPageObjectNumber = flattened.Count > 0 ? flattened[0] : FindFirstPage(pages);
        var acroForm = ReadAcroForm(CatalogEntries);
        AcroFormEntries = acroForm.Entries;
        ExistingAcroFormFields = acroForm.Fields;
    }

    private List<int> FlattenPageObjectNumbers()
    {
        var pages = new List<int>();
        CollectPages(PagesObjectNumber, pages, []);
        return pages;
    }

    private void CollectPages(int current, List<int> pages, HashSet<int> visited)
    {
        if (!visited.Add(current))
        {
            throw new InvalidOperationException("PDF page tree contains a cycle.");
        }

        var dict = GetDictionary(current);
        var type = dict.TryGetValue("/Type", out var typeRaw) ? typeRaw.Trim() : string.Empty;
        if (type is "/Page")
        {
            pages.Add(current);
            return;
        }

        var kids = ResolveReferenceArray(dict, "/Kids");
        if (kids.Count == 0 && type is not "/Pages")
        {
            throw new InvalidOperationException($"PDF object {current} is not a page tree node.");
        }

        foreach (var kid in kids)
        {
            CollectPages(kid, pages, visited);
        }
    }

    private static bool TryReadRectangle(
        IReadOnlyDictionary<string, string> dictionary,
        string key,
        out PdfRectangle rectangle)
    {
        rectangle = default;
        if (!dictionary.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var input = new PdfInput(Encoding.Latin1.GetBytes(raw.Trim()));
        input.SkipWhitespaceAndComments();
        if (input.IsEof || input.Peek() != (byte)'[')
        {
            return false;
        }

        var items = input.ReadArrayItems();
        if (items.Count != 4)
        {
            return false;
        }

        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            var itemReader = new PdfInput(Encoding.Latin1.GetBytes(items[i]));
            if (!itemReader.TryReadNumber(out var number))
            {
                return false;
            }

            values[i] = number;
        }

        rectangle = new PdfRectangle(values[0], values[1], values[2], values[3]);
        return true;
    }

    private int FindFirstPage(int pagesObjectNumber)
    {
        var current = pagesObjectNumber;
        var visited = new HashSet<int>();
        while (visited.Add(current))
        {
            var dict = GetDictionary(current);
            var type = dict.TryGetValue("/Type", out var typeRaw) ? typeRaw.Trim() : string.Empty;
            if (type is "/Page")
            {
                return current;
            }

            if (type is not "/Pages" && !dict.ContainsKey("/Kids"))
            {
                throw new InvalidOperationException($"PDF object {current} is not a page tree node.");
            }

            var kids = ResolveReferenceArray(dict, "/Kids");
            if (kids.Count == 0)
            {
                throw new InvalidOperationException($"PDF page tree object {current} has an empty /Kids array.");
            }

            current = kids[0];
        }

        throw new InvalidOperationException("PDF page tree contains a cycle.");
    }

    private (IReadOnlyDictionary<string, string> Entries, IReadOnlyList<string> Fields) ReadAcroForm(
        IReadOnlyDictionary<string, string> catalog)
    {
        if (!catalog.TryGetValue("/AcroForm", out var acroRaw))
        {
            return (new Dictionary<string, string>(StringComparer.Ordinal), []);
        }

        Dictionary<string, string>? acro = null;
        if (PdfInput.TryParseReference(acroRaw, out var acroObject, out _))
        {
            acro = GetDictionary(acroObject);
        }
        else if (acroRaw.TrimStart().StartsWith("<<", StringComparison.Ordinal))
        {
            acro = new PdfInput(Encoding.Latin1.GetBytes(acroRaw)).ReadDictionary();
        }

        if (acro is null)
        {
            return (new Dictionary<string, string>(StringComparer.Ordinal), []);
        }

        var fields = ResolveReferenceArray(acro, "/Fields");
        var fieldRefs = fields.Select(static n => string.Create(CultureInfo.InvariantCulture, $"{n} 0 R")).ToList();
        return (acro, fieldRefs);
    }

    private List<int> ResolveReferenceArray(IReadOnlyDictionary<string, string> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        raw = raw.Trim();
        if (PdfInput.TryParseReference(raw, out var objectNumber, out _) && !raw.TrimStart().StartsWith('['))
        {
            var value = ReadObjectValue(objectNumber);
            if (value.ArrayItems is not null)
            {
                return ParseObjectNumbers(value.ArrayItems);
            }

            if (value.Raw is not null)
            {
                return PdfInput.ParseReferenceArray(value.Raw);
            }
        }

        return PdfInput.ParseReferenceArray(raw);
    }

    private static List<int> ParseObjectNumbers(IReadOnlyList<string> items)
    {
        var result = new List<int>(items.Count);
        foreach (var item in items)
        {
            if (PdfInput.TryParseReference(item, out var objectNumber, out _))
            {
                result.Add(objectNumber);
            }
        }

        return result;
    }

    private (Dictionary<int, PdfXrefEntry> Entries, Dictionary<string, string> Trailer, int? Prev) ReadXrefSection(
        int offset)
    {
        var input = new PdfInput(_pdf, offset);
        input.SkipWhitespaceAndComments();
        if (input.StartsWith("xref"u8))
        {
            return ReadClassicXref(input);
        }

        try
        {
            return ReadXrefStream(offset);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // Hand-written PDFs often miscount startxref by a few bytes (e.g. pointing into the
            // table instead of at the `xref` keyword). Scan nearby before failing.
            var recovered = TryFindClassicXrefNear(offset);
            if (recovered is int xrefOffset)
            {
                return ReadClassicXref(new PdfInput(_pdf, xrefOffset));
            }

            throw;
        }
    }

    private int? TryFindClassicXrefNear(int offset)
    {
        var needle = "xref"u8;
        var max = Math.Min(_pdf.Length - needle.Length, Math.Max(offset, 0));
        var min = Math.Max(0, offset - 64);
        for (var i = max; i >= min; i--)
        {
            if (!_pdf.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                continue;
            }

            if (i > 0 && !PdfInput.IsWhitespace(_pdf[i - 1]) && !PdfInput.IsDelimiter(_pdf[i - 1]))
            {
                continue;
            }

            var after = i + needle.Length;
            if (after < _pdf.Length && !PdfInput.IsWhitespace(_pdf[after]) && !PdfInput.IsDelimiter(_pdf[after]))
            {
                continue;
            }

            return i;
        }

        return null;
    }

    private static (Dictionary<int, PdfXrefEntry> Entries, Dictionary<string, string> Trailer, int? Prev) ReadClassicXref(
        PdfInput input)
    {
        if (!input.TryConsumeKeyword("xref"u8))
        {
            throw new InvalidOperationException("Expected classic xref table.");
        }

        var entries = new Dictionary<int, PdfXrefEntry>();
        while (true)
        {
            input.SkipWhitespaceAndComments();
            if (input.StartsWith("trailer"u8))
            {
                break;
            }

            if (input.IsEof)
            {
                throw new InvalidOperationException("Classic xref table is missing a trailer.");
            }

            var first = (int)input.ReadNumber();
            var count = (int)input.ReadNumber();
            for (var i = 0; i < count; i++)
            {
                input.SkipWhitespaceAndComments();
                var objectOffset = (int)input.ReadNumber();
                var generation = (int)input.ReadNumber();
                input.SkipWhitespaceAndComments();
                if (input.IsEof)
                {
                    throw new InvalidOperationException("Truncated xref entry.");
                }

                var flag = (char)input.Data[input.Position];
                input.Position++;
                var objectNumber = first + i;
                if (flag is 'n' or 'N')
                {
                    entries[objectNumber] = PdfXrefEntry.Uncompressed(objectNumber, generation, objectOffset);
                }
                else
                {
                    entries[objectNumber] = PdfXrefEntry.Free(objectNumber, generation);
                }
            }
        }

        if (!input.TryConsumeKeyword("trailer"u8))
        {
            throw new InvalidOperationException("Classic xref table is missing a trailer.");
        }

        var trailer = input.ReadDictionary();
        return (entries, trailer, ReadPrev(trailer));
    }

    private (Dictionary<int, PdfXrefEntry> Entries, Dictionary<string, string> Trailer, int? Prev) ReadXrefStream(
        int offset)
    {
        var stream = ReadStreamAtOffset(offset);
        var trailer = stream.Dictionary
            ?? throw new InvalidOperationException("XRef stream is missing a dictionary.");
        var encoded = stream.Encoded
            ?? throw new InvalidOperationException("XRef stream is missing stream data.");
        if (!trailer.TryGetValue("/Type", out var typeName) || typeName.Trim() is not "/XRef")
        {
            throw new InvalidOperationException("startxref does not point to an xref table or /XRef stream.");
        }

        var decoded = PdfFilters.Decode(encoded, trailer);
        var widths = ReadIntArray(trailer, "/W");
        if (widths.Count != 3)
        {
            throw new InvalidOperationException("XRef stream /W must contain three integers.");
        }

        var entrySize = widths.Sum();
        if (entrySize <= 0)
        {
            throw new InvalidOperationException("XRef stream /W entry size is invalid.");
        }

        var index = ReadIntArray(trailer, "/Index");
        if (index.Count == 0)
        {
            var size = TryReadInt(trailer, "/Size", out var trailerSize) ? trailerSize : decoded.Length / entrySize;
            index = [0, size];
        }

        if (index.Count % 2 != 0)
        {
            throw new InvalidOperationException("XRef stream /Index must contain pairs of integers.");
        }

        var entries = new Dictionary<int, PdfXrefEntry>();
        var cursor = 0;
        for (var i = 0; i < index.Count; i += 2)
        {
            var first = index[i];
            var count = index[i + 1];
            for (var n = 0; n < count; n++)
            {
                if (cursor + entrySize > decoded.Length)
                {
                    throw new InvalidOperationException("XRef stream is shorter than /Index requires.");
                }

                var entryType = widths[0] == 0 ? 1 : ReadPacked(decoded, cursor, widths[0]);
                cursor += widths[0];
                var field2 = ReadPacked(decoded, cursor, widths[1]);
                cursor += widths[1];
                var field3 = ReadPacked(decoded, cursor, widths[2]);
                cursor += widths[2];

                var objectNumber = first + n;
                entries[objectNumber] = entryType switch
                {
                    0 => PdfXrefEntry.Free(objectNumber, field3),
                    1 => PdfXrefEntry.Uncompressed(objectNumber, field3, field2),
                    2 => PdfXrefEntry.Compressed(objectNumber, field2, field3),
                    _ => throw new InvalidOperationException($"Unsupported XRef stream entry type {entryType}.")
                };
            }
        }

        return (entries, trailer, ReadPrev(trailer));
    }

    private ObjectValue ReadObjectValue(int objectNumber)
    {
        if (!_xref.TryGetValue(objectNumber, out var entry) || entry.IsFree)
        {
            throw new InvalidOperationException($"PDF object {objectNumber} was not found in the xref table.");
        }

        if (entry.IsCompressed)
        {
            return ReadCompressedObject(entry);
        }

        var offset = ResolveUncompressedOffset(entry.Offset, objectNumber, entry.Generation);
        if (offset != entry.Offset)
        {
            _xref[objectNumber] = PdfXrefEntry.Uncompressed(objectNumber, entry.Generation, offset);
        }

        return ReadUncompressedObject(offset);
    }

    /// <summary>
    /// Hand-written and line-ending-converted PDFs often store xref offsets a few bytes off.
    /// Prefer the claimed offset, then the nearest header within a small window, then the last
    /// <c>n g obj</c> in the file (incremental updates).
    /// </summary>
    private int ResolveUncompressedOffset(int claimedOffset, int objectNumber, int generation)
    {
        if (TryMatchObjectHeader(claimedOffset, objectNumber, generation))
        {
            return claimedOffset;
        }

        const int nearbyWindow = 256;
        var headers = _scannedObjectHeaders ??= ScanObjectHeaders();
        if (!TryGetHeaderOffsets(headers, objectNumber, generation, out var offsets))
        {
            throw new InvalidOperationException(
                $"Expected PDF object {objectNumber} {generation} obj at offset {claimedOffset}.");
        }

        var nearest = offsets[0];
        var nearestDistance = Math.Abs(nearest - claimedOffset);
        foreach (var candidate in offsets)
        {
            var distance = Math.Abs(candidate - claimedOffset);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        if (nearestDistance <= nearbyWindow)
        {
            return nearest;
        }

        return offsets[^1];
    }

    private static bool TryGetHeaderOffsets(
        Dictionary<(int ObjectNumber, int Generation), List<int>> headers,
        int objectNumber,
        int generation,
        out List<int> offsets)
    {
        if (headers.TryGetValue((objectNumber, generation), out offsets!) && offsets.Count > 0)
        {
            return true;
        }

        offsets = [];
        foreach (var entry in headers)
        {
            if (entry.Key.ObjectNumber == objectNumber)
            {
                offsets.AddRange(entry.Value);
            }
        }

        return offsets.Count > 0;
    }

    private bool TryMatchObjectHeader(int position, int objectNumber, int generation)
    {
        if ((uint)position >= (uint)_pdf.Length)
        {
            return false;
        }

        if (position > 0 && PdfInput.IsDigit(_pdf[position]) && PdfInput.IsDigit(_pdf[position - 1]))
        {
            return false;
        }

        var input = new PdfInput(_pdf, position);
        input.SkipWhitespaceAndComments();
        if (!input.TryReadNumber(out var parsedObject) || parsedObject != objectNumber)
        {
            return false;
        }

        if (!input.TryReadNumber(out var parsedGeneration) || parsedGeneration != generation)
        {
            return false;
        }

        return input.TryConsumeKeyword("obj"u8);
    }

    private Dictionary<(int ObjectNumber, int Generation), List<int>> ScanObjectHeaders()
    {
        var headers = new Dictionary<(int ObjectNumber, int Generation), List<int>>();
        var input = new PdfInput(_pdf);
        for (var i = 0; i < _pdf.Length; i++)
        {
            if (!PdfInput.IsDigit(_pdf[i]))
            {
                continue;
            }

            if (i > 0 && PdfInput.IsDigit(_pdf[i - 1]))
            {
                continue;
            }

            input.Position = i;
            if (!input.TryReadNumber(out var objectNumber)
                || objectNumber is < 0 or > int.MaxValue)
            {
                continue;
            }

            if (!input.TryReadNumber(out var generation)
                || generation is < 0 or > int.MaxValue)
            {
                continue;
            }

            if (!input.TryConsumeKeyword("obj"u8))
            {
                continue;
            }

            var key = ((int)objectNumber, (int)generation);
            if (!headers.TryGetValue(key, out var offsets))
            {
                offsets = [];
                headers[key] = offsets;
            }

            offsets.Add(i);
        }

        return headers;
    }

    private ObjectValue ReadUncompressedObject(int offset)
    {
        var input = new PdfInput(_pdf, offset);
        input.SkipWhitespaceAndComments();
        _ = input.ReadNumber();
        _ = input.ReadNumber();
        if (!input.TryConsumeKeyword("obj"u8))
        {
            throw new InvalidDataException($"Expected 'obj' at offset {offset}.");
        }

        input.SkipWhitespaceAndComments();
        if (input.StartsWith("<<"u8))
        {
            var dictionary = input.ReadDictionary();
            input.SkipWhitespaceAndComments();
            if (input.StartsWith("stream"u8))
            {
                var length = ReadStreamLength(dictionary);
                input.ConsumeStreamKeywordNewLine();
                var encoded = input.ReadBytes(length);
                return new ObjectValue(dictionary, encoded, null, null);
            }

            return new ObjectValue(dictionary, null, null, null);
        }

        if (!input.IsEof && input.Peek() == (byte)'[')
        {
            var items = input.ReadArrayItems();
            return new ObjectValue(null, null, items, null);
        }

        var rawStart = input.Position;
        input.SkipValue();
        var raw = Encoding.Latin1.GetString(_pdf, rawStart, input.Position - rawStart).Trim();
        return new ObjectValue(null, null, null, raw);
    }

    private ObjectValue ReadStreamAtOffset(int offset)
    {
        var value = ReadUncompressedObject(offset);
        if (value.Dictionary is null || value.Encoded is null)
        {
            throw new InvalidOperationException($"PDF object at offset {offset} is not a stream.");
        }

        return value;
    }

    private ObjectValue ReadCompressedObject(PdfXrefEntry entry)
    {
        var decoded = GetObjectStreamBytes(entry.ObjectStreamNumber);
        var streamDict = GetDictionary(entry.ObjectStreamNumber);
        if (!TryReadInt(streamDict, "/N", out var count) || !TryReadInt(streamDict, "/First", out var first))
        {
            throw new InvalidOperationException(
                $"Object stream {entry.ObjectStreamNumber} is missing /N or /First.");
        }

        var header = new PdfInput(decoded);
        var offsets = new int[count];
        for (var i = 0; i < count; i++)
        {
            _ = header.ReadNumber();
            offsets[i] = (int)header.ReadNumber();
        }

        if (entry.ObjectStreamIndex < 0 || entry.ObjectStreamIndex >= count)
        {
            throw new InvalidOperationException(
                $"Compressed object {entry.ObjectNumber} has an invalid object-stream index.");
        }

        var start = first + offsets[entry.ObjectStreamIndex];
        var end = entry.ObjectStreamIndex + 1 < count
            ? first + offsets[entry.ObjectStreamIndex + 1]
            : decoded.Length;
        if (start < 0 || end > decoded.Length || start > end)
        {
            throw new InvalidOperationException($"Compressed object {entry.ObjectNumber} is outside its object stream.");
        }

        var slice = decoded.AsSpan(start, end - start).ToArray();
        var input = new PdfInput(slice);
        input.SkipWhitespaceAndComments();
        if (input.StartsWith("<<"u8))
        {
            return new ObjectValue(input.ReadDictionary(), null, null, null);
        }

        if (!input.IsEof && input.Peek() == (byte)'[')
        {
            return new ObjectValue(null, null, input.ReadArrayItems(), null);
        }

        var rawStart = input.Position;
        input.SkipValue();
        var raw = Encoding.Latin1.GetString(slice, rawStart, input.Position - rawStart).Trim();
        return new ObjectValue(null, null, null, raw);
    }

    private byte[] GetObjectStreamBytes(int objectStreamNumber)
    {
        if (_objectStreamCache.TryGetValue(objectStreamNumber, out var cached))
        {
            return cached;
        }

        var stream = ReadObjectValue(objectStreamNumber);
        if (stream.Dictionary is null || stream.Encoded is null)
        {
            throw new InvalidOperationException($"PDF object {objectStreamNumber} is not an object stream.");
        }

        var decoded = PdfFilters.Decode(stream.Encoded, stream.Dictionary);
        _objectStreamCache[objectStreamNumber] = decoded;
        return decoded;
    }

    private int ReadStreamLength(IReadOnlyDictionary<string, string> dictionary)
    {
        if (!dictionary.TryGetValue("/Length", out var raw))
        {
            throw new InvalidOperationException("PDF stream is missing /Length.");
        }

        if (TryReadInt(dictionary, "/Length", out var length))
        {
            return length;
        }

        if (!PdfInput.TryParseReference(raw, out var lengthObject, out _))
        {
            throw new InvalidOperationException("PDF stream /Length is invalid.");
        }

        var value = ReadObjectValue(lengthObject);
        var input = new PdfInput(Encoding.Latin1.GetBytes(value.Raw ?? value.ArrayItems?[0] ?? "0"));
        return (int)input.ReadNumber();
    }

    private static int FindLastStartXref(byte[] pdf)
    {
        var needle = "startxref"u8;
        for (var i = pdf.Length - needle.Length; i >= 0; i--)
        {
            if (!pdf.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                continue;
            }

            if (i > 0 && !PdfInput.IsWhitespace(pdf[i - 1]) && !PdfInput.IsDelimiter(pdf[i - 1]))
            {
                continue;
            }

            var input = new PdfInput(pdf, i + needle.Length);
            if (!input.TryReadNumber(out var offset) || offset < 0 || offset >= pdf.Length)
            {
                continue;
            }

            return (int)offset;
        }

        throw new InvalidOperationException("PDF is missing startxref.");
    }

    private static int? ReadPrev(IReadOnlyDictionary<string, string> trailer) =>
        TryReadInt(trailer, "/Prev", out var prev) ? prev : null;

    private static bool TryReadInt(IReadOnlyDictionary<string, string> dictionary, string key, out int value)
    {
        value = 0;
        if (!dictionary.TryGetValue(key, out var raw))
        {
            return false;
        }

        var input = new PdfInput(Encoding.Latin1.GetBytes(raw));
        if (!input.TryReadNumber(out var number) || number is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        // A reference is not a direct integer (`5 0 R`).
        if (input.TryReadNumber(out _) && input.TryConsumeKeyword("R"u8))
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    private static List<int> ReadIntArray(IReadOnlyDictionary<string, string> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var input = new PdfInput(Encoding.Latin1.GetBytes(raw.Trim()));
        input.SkipWhitespaceAndComments();
        var values = new List<int>();
        if (!input.IsEof && input.Peek() == (byte)'[')
        {
            foreach (var item in input.ReadArrayItems())
            {
                var itemReader = new PdfInput(Encoding.Latin1.GetBytes(item));
                if (itemReader.TryReadNumber(out var number))
                {
                    values.Add((int)number);
                }
            }

            return values;
        }

        while (input.TryReadNumber(out var number))
        {
            values.Add((int)number);
        }

        return values;
    }

    private static int ReadPacked(byte[] data, int offset, int width)
    {
        if (width == 0)
        {
            return 0;
        }

        var value = 0;
        for (var i = 0; i < width; i++)
        {
            value = (value << 8) | data[offset + i];
        }

        return value;
    }

    private sealed record ObjectValue(
        Dictionary<string, string>? Dictionary,
        byte[]? Encoded,
        List<string>? ArrayItems,
        string? Raw);

    private readonly struct PdfXrefEntry
    {
        private PdfXrefEntry(int objectNumber, int generation, int offset, int objectStreamNumber, int objectStreamIndex, bool isFree, bool isCompressed)
        {
            ObjectNumber = objectNumber;
            Generation = generation;
            Offset = offset;
            ObjectStreamNumber = objectStreamNumber;
            ObjectStreamIndex = objectStreamIndex;
            IsFree = isFree;
            IsCompressed = isCompressed;
        }

        public int ObjectNumber { get; }

        public int Generation { get; }

        public int Offset { get; }

        public int ObjectStreamNumber { get; }

        public int ObjectStreamIndex { get; }

        public bool IsFree { get; }

        public bool IsCompressed { get; }

        public static PdfXrefEntry Free(int objectNumber, int generation) =>
            new(objectNumber, generation, 0, 0, 0, isFree: true, isCompressed: false);

        public static PdfXrefEntry Uncompressed(int objectNumber, int generation, int offset) =>
            new(objectNumber, generation, offset, 0, 0, isFree: false, isCompressed: false);

        public static PdfXrefEntry Compressed(int objectNumber, int objectStreamNumber, int index) =>
            new(objectNumber, 0, 0, objectStreamNumber, index, isFree: false, isCompressed: true);
    }
}
