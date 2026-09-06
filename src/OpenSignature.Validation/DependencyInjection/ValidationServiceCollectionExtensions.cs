using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Validation.Certificates;
using OpenSignature.Validation.Reports;
using OpenSignature.Validation.Revocation;
using OpenSignature.Validation.Signatures;

namespace OpenSignature.Validation;

/// <summary>DI helpers for OpenSignature certificate and signature validation.</summary>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers certificate/signature validators, report builder, and a revocation checker.
    /// Default revocation mode is Offline (stub-friendly; no live OCSP/CRL).
    /// </summary>
    public static IServiceCollection AddOpenSignatureValidation(
        this IServiceCollection services,
        RevocationMode revocationMode = RevocationMode.Offline)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRevocationChecker>(_ => new CompositeRevocationChecker(revocationMode));
        services.TryAddSingleton<ICertificateValidator>(sp =>
            new CertificateValidator(sp.GetRequiredService<IRevocationChecker>()));
        services.TryAddSingleton<ISignatureValidator>(sp =>
            new SignatureValidator(sp.GetRequiredService<ICertificateValidator>()));
        services.TryAddSingleton<IValidationReportBuilder, ValidationReportBuilder>();

        return services;
    }
}
