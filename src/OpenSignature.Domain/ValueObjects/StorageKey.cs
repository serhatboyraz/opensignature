namespace OpenSignature.Domain.ValueObjects;

public sealed record StorageKey
{
    public string Value { get; }

    private StorageKey(string value) => Value = value;

    public static StorageKey Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Storage key must not be empty.", nameof(value));
        }

        var trimmed = value.Trim();

        if (trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.Contains("\\", StringComparison.Ordinal)
            || trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.Contains("//", StringComparison.Ordinal)
            || Path.IsPathRooted(trimmed))
        {
            throw new ArgumentException(
                "Storage key must be a relative path without traversal segments or backslashes.",
                nameof(value));
        }

        return new StorageKey(trimmed);
    }

    public override string ToString() => Value;
}
