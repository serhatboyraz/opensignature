using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using OpenSignature.Application.Security;

namespace OpenSignature.Api.Security;

/// <summary>
/// Returns RFC 7807 Problem Details with stable error codes for authn/authz failures.
/// </summary>
public sealed class ProblemDetailsAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Authentication required",
                    "A valid API key is required. Supply Authorization: ApiKey <key> or X-Api-Key.",
                    SecurityErrorCodes.AuthUnauthorized)
                .ConfigureAwait(false);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "The authenticated principal is not permitted to perform this action.",
                    SecurityErrorCodes.AuthForbidden)
                .ConfigureAwait(false);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string errorCode)
    {
        context.Response.ContentType = "application/problem+json";
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
                context.Response.Body,
                problem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            .ConfigureAwait(false);
    }

    private static string ActivityTraceId()
        => System.Diagnostics.Activity.Current?.Id
           ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}
