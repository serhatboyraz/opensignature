using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OpenSignature.Application.Security;

namespace OpenSignature.Api.Security;

/// <summary>
/// Rejects requests where <c>X-Tenant-Id</c> differs from the authenticated API key tenant.
/// </summary>
public sealed class TenantIsolationMiddleware
{
    public const string TenantHeaderName = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    public TenantIsolationMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var principalTenant = user.FindFirst(OpenSignatureClaimTypes.TenantId)?.Value;
            if (!string.IsNullOrWhiteSpace(principalTenant) &&
                context.Request.Headers.TryGetValue(TenantHeaderName, out var tenantHeader) &&
                !string.IsNullOrWhiteSpace(tenantHeader))
            {
                var headerTenant = tenantHeader.ToString().Trim();
                if (!string.Equals(headerTenant, principalTenant.Trim(), StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/problem+json";
                    var problem = new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Tenant access denied",
                        Detail =
                            "X-Tenant-Id does not match the tenant bound to the authenticated API key.",
                        Type = "https://httpstatuses.com/403",
                        Extensions =
                        {
                            ["errorCode"] = SecurityErrorCodes.TenantAccessDenied,
                            ["traceId"] = ActivityTraceId()
                        }
                    };

                    await JsonSerializer.SerializeAsync(
                            context.Response.Body,
                            problem,
                            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                        .ConfigureAwait(false);
                    return;
                }
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static string ActivityTraceId()
        => System.Diagnostics.Activity.Current?.Id
           ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}
