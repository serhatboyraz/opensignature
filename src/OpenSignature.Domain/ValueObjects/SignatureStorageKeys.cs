namespace OpenSignature.Domain.ValueObjects;

/// <summary>
/// Builds server-generated storage keys for signature input and output objects.
/// </summary>
public static class SignatureStorageKeys
{
    public const string InputFileName = "input.bin";

    public const string SignedFileName = "signed.bin";

    public const string AppearanceFileName = "appearance.bin";

    /// <summary>
    /// Builds <c>tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/input.bin</c>.
    /// </summary>
    public static StorageKey ForInput(TenantId tenantId, Guid signatureId, DateTimeOffset utcTimestamp)
        => Build(tenantId, signatureId, utcTimestamp, InputFileName);

    /// <summary>
    /// Builds <c>tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/signed.bin</c>.
    /// </summary>
    public static StorageKey ForSigned(TenantId tenantId, Guid signatureId, DateTimeOffset utcTimestamp)
        => Build(tenantId, signatureId, utcTimestamp, SignedFileName);

    /// <summary>
    /// Builds <c>tenants/{tenantId}/signatures/{yyyy}/{MM}/{dd}/{signatureId}/appearance.bin</c>.
    /// Optional visible-signature image; never placed on the signing queue.
    /// </summary>
    public static StorageKey ForAppearance(TenantId tenantId, Guid signatureId, DateTimeOffset utcTimestamp)
        => Build(tenantId, signatureId, utcTimestamp, AppearanceFileName);

    private static StorageKey Build(
        TenantId tenantId,
        Guid signatureId,
        DateTimeOffset utcTimestamp,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        if (signatureId == Guid.Empty)
        {
            throw new ArgumentException("Signature ID must not be empty.", nameof(signatureId));
        }

        EnsureSafePathSegment(tenantId.Value, nameof(tenantId));

        var utc = utcTimestamp.UtcDateTime;
        var key =
            $"tenants/{tenantId.Value}/signatures/{utc:yyyy}/{utc:MM}/{utc:dd}/{signatureId:D}/{fileName}";

        return StorageKey.Create(key);
    }

    private static void EnsureSafePathSegment(string value, string paramName)
    {
        if (value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Value must not contain path separators or traversal segments.",
                paramName);
        }
    }
}
