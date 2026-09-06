using System.Globalization;
using System.Text;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>PDF literal strings for WinAnsi/Helvetica appearance text.</summary>
internal static class PdfLiteral
{
    public static string String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 8);
        builder.Append('(');
        foreach (var ch in value)
        {
            if (ch is '(' or ')' or '\\')
            {
                builder.Append('\\');
                builder.Append(ch);
                continue;
            }

            if (ch is '\r')
            {
                builder.Append("\\r");
                continue;
            }

            if (ch is '\n')
            {
                builder.Append("\\n");
                continue;
            }

            if (ch < 32 || ch > 126)
            {
                var b = ch > 255 ? (byte)'?' : (byte)ch;
                builder.Append('\\');
                builder.Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                continue;
            }

            builder.Append(ch);
        }

        builder.Append(')');
        return builder.ToString();
    }

    public static bool TryDecodeString(string raw, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();
        if (raw.Length < 2 || raw[0] != '(' || raw[^1] != ')')
        {
            return false;
        }

        var inner = raw[1..^1];
        var builder = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            var ch = inner[i];
            if (ch != '\\')
            {
                builder.Append(ch);
                continue;
            }

            if (i + 1 >= inner.Length)
            {
                break;
            }

            var next = inner[++i];
            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case '(':
                case ')':
                case '\\':
                    builder.Append(next);
                    break;
                default:
                    if (next is >= '0' and <= '7')
                    {
                        var octal = next - '0';
                        var digits = 1;
                        while (digits < 3 && i + 1 < inner.Length && inner[i + 1] is >= '0' and <= '7')
                        {
                            octal = (octal * 8) + (inner[++i] - '0');
                            digits++;
                        }

                        builder.Append((char)octal);
                    }
                    else
                    {
                        builder.Append(next);
                    }

                    break;
            }
        }

        value = builder.ToString();
        return true;
    }

    public static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string CommonNameFromSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return "Unknown signer";
        }

        foreach (var part in subject.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 3)
            {
                return trimmed[3..].Trim();
            }
        }

        return subject.Trim();
    }
}
