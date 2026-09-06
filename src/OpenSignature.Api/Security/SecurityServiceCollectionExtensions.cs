using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Application.Security;

namespace OpenSignature.Api.Security;

/// <summary>
/// Registers MVP API-key authentication, RBAC policies, and Problem Details auth failure handling.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddOpenSignatureSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ApiAuthenticationOptions>(
            configuration.GetSection(ApiAuthenticationOptions.SectionName));

        services
            .AddAuthentication(ApiKeyAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                OpenSignaturePolicies.SignaturesWrite,
                policy => policy.RequireRole(
                    OpenSignatureRoles.Administrator,
                    OpenSignatureRoles.Signer));

            options.AddPolicy(
                OpenSignaturePolicies.SignaturesRead,
                policy => policy.RequireRole(
                    OpenSignatureRoles.Administrator,
                    OpenSignatureRoles.Signer,
                    OpenSignatureRoles.Operator,
                    OpenSignatureRoles.Auditor));

            options.AddPolicy(
                OpenSignaturePolicies.SignaturesCancel,
                policy => policy.RequireRole(
                    OpenSignatureRoles.Administrator,
                    OpenSignatureRoles.Signer,
                    OpenSignatureRoles.Operator));

            options.AddPolicy(
                OpenSignaturePolicies.CertificatesRead,
                policy => policy.RequireRole(
                    OpenSignatureRoles.Administrator,
                    OpenSignatureRoles.Developer));

            options.AddPolicy(
                OpenSignaturePolicies.ProvidersRead,
                policy => policy.RequireRole(
                    OpenSignatureRoles.Administrator,
                    OpenSignatureRoles.Developer,
                    OpenSignatureRoles.Operator));
        });

        services.TryAddSingleton<IAuthorizationMiddlewareResultHandler,
            ProblemDetailsAuthorizationMiddlewareResultHandler>();

        return services;
    }
}
