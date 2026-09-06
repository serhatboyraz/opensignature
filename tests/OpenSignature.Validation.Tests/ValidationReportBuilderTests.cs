using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Pfx;
using OpenSignature.Validation.Certificates;
using OpenSignature.Validation.Reports;
using OpenSignature.Validation.Revocation;
using OpenSignature.Validation.Signatures;

namespace OpenSignature.Validation.Tests;

public sealed class ValidationReportBuilderTests
{
    private const string Password = "validation-report-test-password";

    [Fact]
    public async Task Report_from_valid_signature_is_VALID_and_json_serializable()
    {
        using var material = EphemeralPfx.CreateRsa("CN=Report Valid", Password);
        await using var provider = new PfxSigningProvider(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-report",
            Name = "Report PFX",
            CertificateBytes = material.PfxBytes,
            Password = Password
        });
        var certs = await provider.ListCertificatesAsync();
        var selector = SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
        var content = Encoding.UTF8.GetBytes("report");

        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Detached, provider, selector);

        var signatureResult = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.CAdES, signed.CmsBytes, content));

        var report = new ValidationReportBuilder().Build(signatureResult);

        Assert.Equal(ValidationReportStatus.Valid, report.OverallStatus);
        Assert.True(report.IsValid);
        Assert.NotNull(report.Signature);
        Assert.NotNull(report.Certificate);
        Assert.Equal(SignatureFormat.CAdES, report.Format);

        var json = JsonSerializer.Serialize(report);
        Assert.Contains("VALID", json, StringComparison.Ordinal);
        Assert.Contains("SIG_VALID", json, StringComparison.Ordinal);
        Assert.Contains("CERT_VALID", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_from_expired_certificate_is_INVALID()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Expired Report", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddYears(-2),
            DateTimeOffset.UtcNow.AddDays(-1));

        var options = new CertificateValidationOptions
        {
            UseCustomTrustStore = true,
            RevocationMode = RevocationMode.Offline
        };
        options.TrustAnchors.Add(cert);

        var certResult = await new CertificateValidator().ValidateAsync(cert, options);
        var report = new ValidationReportBuilder().Build(certResult);

        Assert.Equal(ValidationReportStatus.Invalid, report.OverallStatus);
        Assert.False(report.IsValid);
        Assert.Null(report.Signature);
        Assert.Contains(CertificateValidationCodes.CertExpired, report.ReasonCodes);
    }

    [Fact]
    public void Di_registration_resolves_validators()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddOpenSignatureValidation(RevocationMode.Offline);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<ICertificateValidator>());
        Assert.NotNull(sp.GetService<ISignatureValidator>());
        Assert.NotNull(sp.GetService<IValidationReportBuilder>());
        Assert.NotNull(sp.GetService<IRevocationChecker>());
    }
}
