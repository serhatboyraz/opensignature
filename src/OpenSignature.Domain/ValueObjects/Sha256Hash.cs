namespace OpenSignature.Domain.ValueObjects;

public sealed record Sha256Hash
{
    public const int HexLength = 64;

    public string Value { get; }

    private Sha256Hash(string value) => Value = value;

    public static Sha256Hash Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SHA-256 hash must not be empty.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length != HexLength)
        {
            throw new ArgumentException(
                $"SHA-256 hash must be {HexLength} hexadecimal characters.",
                nameof(value));
        }

        if (!IsHex(normalized))
        {
            throw new ArgumentException("SHA-256 hash must contain only hexadecimal characters.", nameof(value));
        }

        return new Sha256Hash(normalized);
    }

    public override string ToString() => Value;

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
