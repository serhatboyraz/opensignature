using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OpenSignature.Api.Security;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Api.Endpoints;

/// <summary>
/// Minimal API routes for public certificate metadata from configured signing providers.
/// Private keys are never exposed.
/// </summary>
public static class CertificateEndpoints
{
    public static IEndpointRouteBuilder MapCertificateEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization = false)
    {
        var group = endpoints.MapGroup("/api/v1/certificates")
            .WithTags("Certificates");

        var list = group.MapGet("/", ListCertificatesAsync)
            .WithName("ListCertificates")
            .WithSummary("List certificates")
            .WithDescription("Lists public certificate metadata visible to configured signing providers.")
            .Produces(StatusCodes.Status200OK);
        if (requireAuthorization)
        {
            list.RequireAuthorization(OpenSignaturePolicies.CertificatesRead);
        }

        var get = group.MapGet("/{id}", GetCertificateAsync)
            .WithName("GetCertificate")
            .WithSummary("Get certificate by thumbprint")
            .WithDescription("Returns public metadata for a certificate identified by thumbprint. Never returns private key material.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        if (requireAuthorization)
        {
            get.RequireAuthorization(OpenSignaturePolicies.CertificatesRead);
        }

        return endpoints;
    }

    private static async Task<IResult> ListCertificatesAsync(
        ISigningProviderResolver providerResolver,
        string? providerId,
        bool? canSign,
        CancellationToken cancellationToken)
    {
        var items = new List<object>();

        foreach (var provider in providerResolver.GetProviders())
        {
            if (!string.IsNullOrWhiteSpace(providerId) &&
                !string.Equals(provider.ProviderId, providerId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<CertificateInfo> certificates;
            try
            {
                certificates = await provider.ListCertificatesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Provider unavailable (e.g. missing PFX) — skip rather than fail the inventory.
                continue;
            }

            foreach (var certificate in certificates)
            {
                if (canSign is not null && certificate.CanSign != canSign.Value)
                {
                    continue;
                }

                items.Add(ToListItem(provider.ProviderId, certificate));
            }
        }

        return Results.Ok(items);
    }

    private static async Task<IResult> GetCertificateAsync(
        string id,
        ISigningProviderResolver providerResolver,
        CancellationToken cancellationToken)
    {
        CertificateThumbprint thumbprint;
        try
        {
            thumbprint = CertificateThumbprint.Create(id);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid certificate id",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var selector = SigningCertificateSelector.ByThumbprint(thumbprint);

        foreach (var provider in providerResolver.GetProviders())
        {
            CertificateInfo? certificate;
            try
            {
                certificate = await provider.GetCertificateAsync(selector, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (certificate is null)
            {
                continue;
            }

            return Results.Ok(ToDetailItem(provider.ProviderId, certificate));
        }

        return Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Certificate not found",
            detail: $"Certificate '{id}' was not found on any configured signing provider.",
            errorCode: "SIGNING_CERTIFICATE_NOT_FOUND");
    }

    private static object ToListItem(string providerId, CertificateInfo certificate) => new
    {
        id = certificate.Thumbprint.Value,
        thumbprint = certificate.Thumbprint.Value,
        subject = certificate.Subject,
        issuer = certificate.Issuer,
        serialNumber = certificate.SerialNumber,
        notBefore = certificate.NotBefore,
        notAfter = certificate.NotAfter,
        providerId,
        providerReference = certificate.ProviderReference,
        canSign = certificate.CanSign,
        friendlyName = certificate.FriendlyName,
        publicKeyAlgorithm = certificate.PublicKeyAlgorithm,
        keySizeBits = certificate.KeySizeBits,
        keyUsages = certificate.KeyUsages,
        enhancedKeyUsages = certificate.EnhancedKeyUsages,
        isCurrentlyValid = certificate.IsCurrentlyValid()
    };

    private static object ToDetailItem(string providerId, CertificateInfo certificate) => new
    {
        id = certificate.Thumbprint.Value,
        thumbprint = certificate.Thumbprint.Value,
        subject = certificate.Subject,
        issuer = certificate.Issuer,
        serialNumber = certificate.SerialNumber,
        notBefore = certificate.NotBefore,
        notAfter = certificate.NotAfter,
        providerId,
        providerReference = certificate.ProviderReference,
        canSign = certificate.CanSign,
        friendlyName = certificate.FriendlyName,
        publicKeyAlgorithm = certificate.PublicKeyAlgorithm,
        keySizeBits = certificate.KeySizeBits,
        keyUsages = certificate.KeyUsages,
        enhancedKeyUsages = certificate.EnhancedKeyUsages,
        isCurrentlyValid = certificate.IsCurrentlyValid(),
        publicCertificateDerBase64 = certificate.PublicCertificateDer.Count == 0
            ? null
            : Convert.ToBase64String(certificate.PublicCertificateDer.ToArray())
    };

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
