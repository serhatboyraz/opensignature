using System.Globalization;
using System.Text;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>Byte-level PDF tokenizer for structural dictionaries, arrays, and numbers.</summary>
internal sealed class PdfInput
{
    private static readonly Encoding Latin1 = Encoding.Latin1;
    private readonly byte[] _data;
    private int _pos;

    public PdfInput(byte[] data, int position = 0)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _pos = position;
    }

    public byte[] Data => _data;

    public int Position
    {
        get => _pos;
        set => _pos = value;
    }

    public int Length => _data.Length;

    public bool IsEof => _pos >= _data.Length;

    public byte Peek() => _pos < _data.Length ? _data[_pos] : (byte)0;

    public int PeekAt(int offset)
    {
        var index = _pos + offset;
        return index >= 0 && index < _data.Length ? _data[index] : -1;
    }

    public bool StartsWith(ReadOnlySpan<byte> ascii) =>
        _pos + ascii.Length <= _data.Length && _data.AsSpan(_pos, ascii.Length).SequenceEqual(ascii);

    public void SkipWhitespaceAndComments()
    {
        while (_pos < _data.Length)
        {
            var b = _data[_pos];
            if (IsWhitespace(b))
            {
                _pos++;
                continue;
            }

            if (b == (byte)'%')
            {
                _pos++;
                while (_pos < _data.Length && _data[_pos] is not (byte)'\n' and not (byte)'\r')
                {
                    _pos++;
                }

                continue;
            }

            break;
        }
    }

    public bool TryConsumeKeyword(ReadOnlySpan<byte> keyword)
    {
        SkipWhitespaceAndComments();
        if (!StartsWith(keyword))
        {
            return false;
        }

        var after = _pos + keyword.Length;
        if (after < _data.Length && !IsDelimiter(_data[after]) && !IsWhitespace(_data[after]))
        {
            return false;
        }

        _pos = after;
        return true;
    }

    public bool TryReadNumber(out long value)
    {
        SkipWhitespaceAndComments();
        var start = _pos;
        if (_pos < _data.Length && _data[_pos] is (byte)'+' or (byte)'-')
        {
            _pos++;
        }

        var digits = 0;
        while (_pos < _data.Length && IsDigit(_data[_pos]))
        {
            digits++;
            _pos++;
        }

        if (_pos < _data.Length && _data[_pos] == (byte)'.')
        {
            _pos++;
            while (_pos < _data.Length && IsDigit(_data[_pos]))
            {
                digits++;
                _pos++;
            }
        }

        if (digits == 0)
        {
            _pos = start;
            value = 0;
            return false;
        }

        var text = Latin1.GetString(_data, start, _pos - start);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            value = (long)real;
            return true;
        }

        _pos = start;
        value = 0;
        return false;
    }

    public long ReadNumber()
    {
        if (!TryReadNumber(out var value))
        {
            throw new InvalidOperationException($"Expected a PDF number at offset {_pos}.");
        }

        return value;
    }

    public string ReadName()
    {
        SkipWhitespaceAndComments();
        if (IsEof || _data[_pos] != (byte)'/')
        {
            throw new InvalidOperationException($"Expected a PDF name at offset {_pos}.");
        }

        var start = _pos;
        _pos++;
        while (_pos < _data.Length && !IsWhitespace(_data[_pos]) && !IsDelimiter(_data[_pos]))
        {
            if (_data[_pos] == (byte)'#' && _pos + 2 < _data.Length && IsHex(_data[_pos + 1]) && IsHex(_data[_pos + 2]))
            {
                _pos += 3;
                continue;
            }

            _pos++;
        }

        return DecodeName(Latin1.GetString(_data, start, _pos - start));
    }

    public Dictionary<string, string> ReadDictionary()
    {
        SkipWhitespaceAndComments();
        if (!StartsWith("<<"u8))
        {
            throw new InvalidOperationException($"Expected a PDF dictionary at offset {_pos}.");
        }

        _pos += 2;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            SkipWhitespaceAndComments();
            if (StartsWith(">>"u8))
            {
                _pos += 2;
                break;
            }

            if (IsEof)
            {
                throw new InvalidOperationException("Unterminated PDF dictionary.");
            }

            var key = ReadName();
            SkipWhitespaceAndComments();
            var valueStart = _pos;
            SkipValue();
            entries[key] = Latin1.GetString(_data, valueStart, _pos - valueStart).Trim();
        }

        return entries;
    }

    public List<string> ReadArrayItems()
    {
        SkipWhitespaceAndComments();
        if (IsEof || _data[_pos] != (byte)'[')
        {
            throw new InvalidOperationException($"Expected a PDF array at offset {_pos}.");
        }

        _pos++;
        var items = new List<string>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (IsEof)
            {
                throw new InvalidOperationException("Unterminated PDF array.");
            }

            if (_data[_pos] == (byte)']')
            {
                _pos++;
                break;
            }

            var start = _pos;
            SkipValue();
            items.Add(Latin1.GetString(_data, start, _pos - start).Trim());
        }

        return items;
    }

    public void SkipValue()
    {
        SkipWhitespaceAndComments();
        if (IsEof)
        {
            throw new InvalidOperationException("Unexpected end of PDF while reading a value.");
        }

        var b = _data[_pos];
        if (b == (byte)'/')
        {
            ReadName();
            return;
        }

        if (b == (byte)'(')
        {
            SkipLiteralString();
            return;
        }

        if (b == (byte)'<')
        {
            if (PeekAt(1) == (byte)'<')
            {
                ReadDictionary();
                return;
            }

            SkipHexString();
            return;
        }

        if (b == (byte)'[')
        {
            ReadArrayItems();
            return;
        }

        if (StartsWith("true"u8) && IsKeywordEnd(4))
        {
            _pos += 4;
            return;
        }

        if (StartsWith("false"u8) && IsKeywordEnd(5))
        {
            _pos += 5;
            return;
        }

        if (StartsWith("null"u8) && IsKeywordEnd(4))
        {
            _pos += 4;
            return;
        }

        var numberStart = _pos;
        if (!TryReadNumber(out _))
        {
            throw new InvalidOperationException($"Unsupported PDF value at offset {_pos}.");
        }

        var saved = _pos;
        SkipWhitespaceAndComments();
        if (TryReadNumber(out _))
        {
            SkipWhitespaceAndComments();
            if (TryConsumeKeyword("R"u8))
            {
                return;
            }
        }

        _pos = saved;
        if (_pos == numberStart)
        {
            throw new InvalidOperationException($"Unsupported PDF value at offset {_pos}.");
        }
    }

    public void ConsumeStreamKeywordNewLine()
    {
        SkipWhitespaceAndComments();
        if (!StartsWith("stream"u8))
        {
            throw new InvalidOperationException($"Expected 'stream' at offset {_pos}.");
        }

        _pos += 6;
        if (_pos < _data.Length && _data[_pos] == (byte)'\r')
        {
            _pos++;
            if (_pos < _data.Length && _data[_pos] == (byte)'\n')
            {
                _pos++;
            }
        }
        else if (_pos < _data.Length && _data[_pos] == (byte)'\n')
        {
            _pos++;
        }
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || _pos + count > _data.Length)
        {
            throw new InvalidOperationException("PDF stream Length exceeds the file size.");
        }

        var slice = _data.AsSpan(_pos, count).ToArray();
        _pos += count;
        return slice;
    }

    public static bool TryParseReference(string raw, out int objectNumber, out int generation)
    {
        objectNumber = 0;
        generation = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var input = new PdfInput(Latin1.GetBytes(raw));
        if (!input.TryReadNumber(out var obj) || obj is < 0 or > int.MaxValue)
        {
            return false;
        }

        if (!input.TryReadNumber(out var gen) || gen is < 0 or > int.MaxValue)
        {
            return false;
        }

        if (!input.TryConsumeKeyword("R"u8))
        {
            return false;
        }

        objectNumber = (int)obj;
        generation = (int)gen;
        return true;
    }

    public static List<int> ParseReferenceArray(string raw)
    {
        var refs = new List<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return refs;
        }

        var input = new PdfInput(Latin1.GetBytes(raw.Trim()));
        input.SkipWhitespaceAndComments();
        if (!input.IsEof && input.Peek() == (byte)'[')
        {
            foreach (var item in input.ReadArrayItems())
            {
                if (TryParseReference(item, out var objectNumber, out _))
                {
                    refs.Add(objectNumber);
                }
            }

            return refs;
        }

        if (TryParseReference(raw, out var single, out _))
        {
            refs.Add(single);
        }

        return refs;
    }

    public static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    public static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    public static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool IsHex(byte b) =>
        b is >= (byte)'0' and <= (byte)'9'
            or >= (byte)'A' and <= (byte)'F'
            or >= (byte)'a' and <= (byte)'f';

    private bool IsKeywordEnd(int length)
    {
        var after = _pos + length;
        return after >= _data.Length || IsDelimiter(_data[after]) || IsWhitespace(_data[after]);
    }

    private void SkipLiteralString()
    {
        if (IsEof || _data[_pos] != (byte)'(')
        {
            throw new InvalidOperationException($"Expected a literal string at offset {_pos}.");
        }

        _pos++;
        var depth = 1;
        while (_pos < _data.Length && depth > 0)
        {
            var b = _data[_pos++];
            if (b == (byte)'\\')
            {
                if (_pos < _data.Length)
                {
                    _pos++;
                }

                continue;
            }

            if (b == (byte)'(')
            {
                depth++;
            }
            else if (b == (byte)')')
            {
                depth--;
            }
        }

        if (depth != 0)
        {
            throw new InvalidOperationException("Unterminated PDF literal string.");
        }
    }

    private void SkipHexString()
    {
        if (IsEof || _data[_pos] != (byte)'<')
        {
            throw new InvalidOperationException($"Expected a hex string at offset {_pos}.");
        }

        _pos++;
        while (_pos < _data.Length && _data[_pos] != (byte)'>')
        {
            _pos++;
        }

        if (IsEof)
        {
            throw new InvalidOperationException("Unterminated PDF hex string.");
        }

        _pos++;
    }

    private static string DecodeName(string name)
    {
        if (!name.Contains('#', StringComparison.Ordinal))
        {
            return name;
        }

        var output = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '#' && i + 2 < name.Length
                && Uri.IsHexDigit(name[i + 1]) && Uri.IsHexDigit(name[i + 2]))
            {
                output.Append((char)Convert.ToInt32(name.AsSpan(i + 1, 2).ToString(), 16));
                i += 2;
            }
            else
            {
                output.Append(name[i]);
            }
        }

        return output.ToString();
    }
}
