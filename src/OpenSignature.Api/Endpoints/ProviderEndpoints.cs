using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OpenSignature.Api.Security;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Api.Endpoints;

/// <summary>
/// Minimal API routes for configured signing providers and health checks.
/// </summary>
public static class ProviderEndpoints
{
    /// <summary>
    /// Maps <c>GET /api/v1/providers</c> and <c>GET /api/v1/providers/{id}/health</c>.
    /// Provider <c>id</c> is matched to <see cref="ISigningProvider.ProviderId"/> using
    /// ordinal case-insensitive comparison (same as <see cref="ISigningProviderResolver"/>).
    /// </summary>
    public static IEndpointRouteBuilder MapProviderEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization = false)
    {
        var group = endpoints.MapGroup("/api/v1/providers")
            .WithTags("Providers");

        var list = group.MapGet("/", ListProviders)
            .WithName("ListProviders")
            .Produces(StatusCodes.Status200OK);
        if (requireAuthorization)
        {
            list.RequireAuthorization(OpenSignaturePolicies.ProvidersRead);
        }

        var health = group.MapGet("/{id}/health", GetProviderHealthAsync)
            .WithName("GetProviderHealth")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        if (requireAuthorization)
        {
            health.RequireAuthorization(OpenSignaturePolicies.ProvidersRead);
        }

        return endpoints;
    }

    private static IResult ListProviders(ISigningProviderResolver providerResolver)
    {
        var items = providerResolver.GetProviders()
            .Select(provider => new
            {
                id = provider.ProviderId,
                name = provider.Name,
                providerType = provider.ProviderType.ToString()
            })
            .ToArray();

        return Results.Ok(items);
    }

    private static async Task<IResult> GetProviderHealthAsync(
        string id,
        ISigningProviderResolver providerResolver,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Signing provider not found",
                detail: "Provider id must not be empty.",
                errorCode: "SIGNING_PROVIDER_UNAVAILABLE");
        }

        var provider = providerResolver.GetProviders()
            .FirstOrDefault(p => string.Equals(p.ProviderId, id.Trim(), StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Signing provider not found",
                detail: $"Signing provider '{id}' is not registered.",
                errorCode: "SIGNING_PROVIDER_UNAVAILABLE");
        }

        var health = await provider.GetHealthAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            id = provider.ProviderId,
            state = health.State.ToString(),
            isHealthy = health.IsHealthy,
            detail = health.Detail,
            checkedAt = health.CheckedAt
        });
    }

    private static IResult Problem(int statusCode, string title, string detail, string errorCode)
        => Results.Problem(
            new ProblemDetails
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
            });

    private static string ActivityTraceId()
        => System.Diagnostics.Activity.Current?.Id
           ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}
