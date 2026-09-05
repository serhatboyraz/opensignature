using System.Text.RegularExpressions;

namespace OpenSignature.Domain.ValueObjects;

public sealed partial record ErrorCode
{
    public string Value { get; }

    private ErrorCode(string value) => Value = value;

    public static ErrorCode Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Error code must not be empty.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (!ErrorCodePattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Error code must use uppercase letters, digits, and underscores (e.g. SIGNING_OPERATION_FAILED).",
                nameof(value));
        }

        return new ErrorCode(normalized);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodePattern();
}
