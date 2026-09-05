namespace OpenSignature.Domain.ValueObjects;

public sealed record CertificateThumbprint
{
    public string Value { get; }

    private CertificateThumbprint(string value) => Value = value;

    public static CertificateThumbprint Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Certificate thumbprint must not be empty.", nameof(value));
        }

        var normalized = value
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

        if (normalized.Length is not (40 or 64))
        {
            throw new ArgumentException(
                "Certificate thumbprint must be 40 (SHA-1) or 64 (SHA-256) hexadecimal characters.",
                nameof(value));
        }

        if (!IsHex(normalized))
        {
            throw new ArgumentException(
                "Certificate thumbprint must contain only hexadecimal characters.",
                nameof(value));
        }

        return new CertificateThumbprint(normalized);
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
