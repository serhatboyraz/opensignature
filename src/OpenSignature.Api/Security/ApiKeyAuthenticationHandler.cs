using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Security;

namespace OpenSignature.Api.Security;

/// <summary>
/// Authenticates requests using <c>Authorization: ApiKey &lt;key&gt;</c> or <c>X-Api-Key</c>.
/// Never logs key material.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<ApiAuthenticationOptions> _authenticationOptions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<ApiAuthenticationOptions> authenticationOptions)
        : base(options, logger, encoder)
    {
        _authenticationOptions = authenticationOptions
            ?? throw new ArgumentNullException(nameof(authenticationOptions));
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_authenticationOptions.CurrentValue.Enabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!TryGetPresentedKey(out var presentedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var match = FindMatchingKey(presentedKey);
        if (match is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim>
        {
            new(OpenSignatureClaimTypes.KeyId, match.KeyId),
            new(OpenSignatureClaimTypes.TenantId, match.TenantId),
            new(ClaimTypes.NameIdentifier, match.KeyId),
            new(ClaimTypes.Name, match.KeyId)
        };

        foreach (var role in match.Roles.Where(static r => !string.IsNullOrWhiteSpace(r)))
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
        }

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await WriteProblemAsync(
                StatusCodes.Status401Unauthorized,
                "Authentication required",
                "A valid API key is required. Supply Authorization: ApiKey <key> or X-Api-Key.",
                SecurityErrorCodes.AuthUnauthorized)
            .ConfigureAwait(false);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await WriteProblemAsync(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "The authenticated principal is not permitted to perform this action.",
                SecurityErrorCodes.AuthForbidden)
            .ConfigureAwait(false);
    }

    private bool TryGetPresentedKey(out string key)
    {
        key = string.Empty;

        if (Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues))
        {
            var headerKey = headerValues.ToString();
            if (!string.IsNullOrWhiteSpace(headerKey))
            {
                key = headerKey.Trim();
                return true;
            }
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        const string prefix = ApiKeyAuthenticationDefaults.AuthorizationScheme + " ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key = authorization[prefix.Length..].Trim();
        return key.Length > 0;
    }

    private ApiKeyOptions? FindMatchingKey(string presentedKey)
    {
        foreach (var candidate in _authenticationOptions.CurrentValue.ApiKeys)
        {
            if (string.IsNullOrWhiteSpace(candidate.Key) ||
                string.IsNullOrWhiteSpace(candidate.KeyId) ||
                string.IsNullOrWhiteSpace(candidate.TenantId))
            {
                continue;
            }

            if (FixedTimeEquals(presentedKey, candidate.Key))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private async Task WriteProblemAsync(int statusCode, string title, string detail, string errorCode)
    {
        Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["traceId"] = ActivityTraceId()
            }
        };

        await JsonSerializer.SerializeAsync(
                Response.Body,
                problem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            .ConfigureAwait(false);
    }

    private static string ActivityTraceId()
        => System.Diagnostics.Activity.Current?.Id
           ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}
