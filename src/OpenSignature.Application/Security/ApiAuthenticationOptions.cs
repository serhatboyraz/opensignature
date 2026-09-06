namespace OpenSignature.Application.Security;

/// <summary>
/// MVP API-key authentication options. Prefer user-secrets or environment variables for key material;
/// never commit real production keys.
/// </summary>
public sealed class ApiAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// When <c>false</c> (default), API endpoints remain anonymous so existing smoke tests pass.
    /// When <c>true</c>, protected routes require a valid API key.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Configured API keys. Load from configuration / user-secrets; never commit real values.
    /// </summary>
    public List<ApiKeyOptions> ApiKeys { get; set; } = [];
}

/// <summary>
/// A single MVP API key binding a tenant and RBAC roles.
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>Stable identifier for the key (logged / audited; not secret).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Secret key material. Never log or commit production values.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Tenant this key is scoped to.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>RBAC roles granted to callers of this key.</summary>
    public string[] Roles { get; set; } = [];
}
