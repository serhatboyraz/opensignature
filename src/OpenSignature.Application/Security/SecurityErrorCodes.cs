namespace OpenSignature.Application.Security;

/// <summary>
/// Stable machine-readable security error codes for Problem Details responses.
/// </summary>
public static class SecurityErrorCodes
{
    public const string AuthUnauthorized = "AUTH_UNAUTHORIZED";
    public const string AuthForbidden = "AUTH_FORBIDDEN";
    public const string TenantAccessDenied = "TENANT_ACCESS_DENIED";
}
