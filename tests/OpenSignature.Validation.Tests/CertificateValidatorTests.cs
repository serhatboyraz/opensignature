using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Crypto;
using OpenSignature.Validation.Certificates;
using OpenSignature.Validation.Revocation;

namespace OpenSignature.Validation.Tests;

public sealed class CertificateValidatorTests
{
    [Fact]
    public async Task Valid_self_signed_with_custom_trust_returns_CERT_VALID()
    {
        using var cert = CreateSigningCert("CN=Valid Cert", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var options = Trusted(cert);

        var result = await new CertificateValidator().ValidateAsync(cert, options);

        Assert.True(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertValid, result.ReasonCodes);
        Assert.Equal(RevocationStatus.Skipped, result.Revocation?.Status);
    }

    [Fact]
    public async Task Expired_certificate_returns_CERT_EXPIRED()
    {
        using var cert = CreateSigningCert("CN=Expired", DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1));
        var options = Trusted(cert);

        var result = await new CertificateValidator().ValidateAsync(cert, options);

        Assert.False(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertExpired, result.ReasonCodes);
    }

    [Fact]
    public async Task Not_yet_valid_returns_CERT_NOT_YET_VALID()
    {
        using var cert = CreateSigningCert("CN=Future", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddYears(1));
        var options = Trusted(cert);

        var result = await new CertificateValidator().ValidateAsync(cert, options);

        Assert.False(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertNotYetValid, result.ReasonCodes);
    }

    [Fact]
    public async Task Untrusted_root_returns_CERT_UNTRUSTED()
    {
        using var cert = CreateSigningCert("CN=Untrusted", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var options = new CertificateValidationOptions
        {
            UseCustomTrustStore = true,
            RevocationMode = RevocationMode.Offline,
            AllowSelfSignedWhenTrusted = false
        };
        // Empty custom trust store → untrusted

        var result = await new CertificateValidator().ValidateAsync(cert, options);

        Assert.False(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertUntrusted, result.ReasonCodes);
    }

    [Fact]
    public async Task Invalid_key_usage_returns_CERT_KEY_USAGE_INVALID()
    {
        using var cert = CreateCertWithKeyUsage(
            "CN=KeyEncipherment Only",
            X509KeyUsageFlags.KeyEncipherment);
        var options = Trusted(cert);

        var result = await new CertificateValidator().ValidateAsync(cert, options);

        Assert.False(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertKeyUsageInvalid, result.ReasonCodes);
    }

    [Fact]
    public async Task Mock_revocation_checker_can_report_CERT_REVOKED()
    {
        using var cert = CreateSigningCert("CN=Revoked", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var options = Trusted(cert);
        var checker = new StubRevocationChecker(new RevocationCheckResult(RevocationStatus.Revoked, "Stub", "revoked for test"));

        var result = await new CertificateValidator(checker).ValidateAsync(cert, options);

        Assert.False(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertRevoked, result.ReasonCodes);
    }

    [Fact]
    public async Task Public_der_overload_validates()
    {
        using var cert = CreateSigningCert("CN=DER", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var der = CertificateHelper.ExportPublicDer(cert);
        var options = Trusted(cert);

        var result = await new CertificateValidator().ValidateAsync(der, options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Invalid_der_returns_CERT_PARSE_FAILED()
    {
        var result = await new CertificateValidator().ValidateAsync(new byte[] { 0x01, 0x02, 0x03 });

        Assert.False(result.IsValid);
        Assert.Contains(CertificateValidationCodes.CertParseFailed, result.ReasonCodes);
    }

    private static CertificateValidationOptions Trusted(X509Certificate2 cert)
    {
        var options = new CertificateValidationOptions
        {
            UseCustomTrustStore = true,
            RevocationMode = RevocationMode.Offline,
            RequireSigningKeyUsage = true,
            AllowSelfSignedWhenTrusted = true
        };
        options.TrustAnchors.Add(cert);
        return options;
    }

    private static X509Certificate2 CreateSigningCert(string subject, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                critical: true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Certificate2 CreateCertWithKeyUsage(string subject, X509KeyUsageFlags flags)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(flags, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private sealed class StubRevocationChecker : IRevocationChecker
    {
        private readonly RevocationCheckResult _result;

        public StubRevocationChecker(RevocationCheckResult result) => _result = result;

        public Task<RevocationCheckResult> CheckAsync(
            X509Certificate2 certificate,
            X509Certificate2? issuer,
            DateTimeOffset asOf,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
