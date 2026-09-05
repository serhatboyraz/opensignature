namespace OpenSignature.Application.Signatures;

/// <summary>
/// API-facing limits and POC defaults for signature requests.
/// </summary>
public sealed class SignatureApiOptions
{
    public const string SectionName = "Signatures";

    /// <summary>Default tenant when <c>X-Tenant-Id</c> is omitted (POC only).</summary>
    public string DefaultTenantId { get; set; } = "tenant-demo";

    /// <summary>Maximum upload size in bytes (default 25 MiB).</summary>
    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;
}
