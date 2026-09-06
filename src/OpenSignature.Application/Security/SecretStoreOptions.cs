namespace OpenSignature.Application.Security;

/// <summary>
/// Named secret values for development (configuration / user-secrets).
/// Never commit real PFX passwords or production secrets.
/// </summary>
public sealed class SecretStoreOptions
{
    public const string SectionName = "Secrets";

    /// <summary>
    /// Map of secret name → value. Prefer user-secrets over checked-in configuration.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
