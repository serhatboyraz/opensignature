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
