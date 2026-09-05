namespace OpenSignature.Domain.ValueObjects;

public sealed record TenantId
{
    public string Value { get; }

    private TenantId(string value) => Value = value;

    public static TenantId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Tenant ID must not be empty.", nameof(value));
        }

        return new TenantId(value.Trim());
    }

    public override string ToString() => Value;
}
