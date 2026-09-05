namespace OpenSignature.Domain.ValueObjects;

public sealed record CorrelationId
{
    public string Value { get; }

    private CorrelationId(string value) => Value = value;

    public static CorrelationId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Correlation ID must not be empty.", nameof(value));
        }

        return new CorrelationId(value.Trim());
    }

    public static CorrelationId New() => new(Guid.CreateVersion7().ToString("N"));

    public override string ToString() => Value;
}
