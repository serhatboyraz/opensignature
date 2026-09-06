namespace OpenSignature.Api.Security;

/// <summary>
/// Authentication scheme name for MVP API keys.
/// </summary>
public static class ApiKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string AuthorizationScheme = "ApiKey";
}
