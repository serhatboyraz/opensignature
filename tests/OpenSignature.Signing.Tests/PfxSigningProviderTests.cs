using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class PfxSigningProviderTests
{
    private const string TestPassword = "unit-test-only-password";

    [Fact]
    public async Task Valid_certificate_signs_digest_and_reports_healthy()
    {
        using var material = EphemeralPfx.CreateRsa(
            subject: "CN=OpenSignature Valid",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            password: TestPassword);

        await using var provider = CreateProvider(material.PfxBytes, TestPassword);

        var certificates = await provider.ListCertificatesAsync();
        var health = await provider.GetHealthAsync();

        Assert.Equal(SigningProviderType.Pfx, provider.ProviderType);
        Assert.Single(certificates);
        Assert.True(certificates[0].CanSign);
        Assert.True(certificates[0].IsCurrentlyValid());
        Assert.Equal("RSA", certificates[0].PublicKeyAlgorithm);
        Assert.NotEmpty(certificates[0].PublicCertificateDer);
        Assert.True(health.IsHealthy);
        Assert.Equal(ProviderHealthState.Healthy, health.State);

        var digest = RandomDigest(DigestAlgorithm.Sha256);
        var signature = await provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByThumbprint(certificates[0].Thumbprint));

        Assert.NotEmpty(signature);
        Assert.True(VerifyRsaSignature(certificates[0], digest, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task Invalid_password_fails_load_and_health_is_unavailable()
    {
        using var material = EphemeralPfx.CreateRsa(
            subject: "CN=OpenSignature Password",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            password: TestPassword);

        await using var provider = CreateProvider(material.PfxBytes, "wrong-password");

        var health = await provider.GetHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.Contains("invalid password", health.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestPassword, health.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", health.Detail ?? string.Empty, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ListCertificatesAsync());
    }

    [Fact]
    public async Task Expired_certificate_is_listed_with_CanSign_false_and_cannot_sign()
    {
        using var material = EphemeralPfx.CreateRsa(
            subject: "CN=OpenSignature Expired",
            notBefore: DateTimeOffset.UtcNow.AddYears(-2),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1),
            password: TestPassword);

        await using var provider = CreateProvider(material.PfxBytes, TestPassword);

        var certificates = await provider.ListCertificatesAsync();
        Assert.Single(certificates);
        Assert.False(certificates[0].CanSign);
        Assert.False(certificates[0].IsCurrentlyValid());

        var health = await provider.GetHealthAsync();
        Assert.Equal(ProviderHealthState.Degraded, health.State);

        var digest = RandomDigest(DigestAlgorithm.Sha256);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByThumbprint(certificates[0].Thumbprint)));

        Assert.Contains("cannot sign", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_certificate_can_sign_when_allow_expired_is_enabled()
    {
        using var material = EphemeralPfx.CreateRsa(
            subject: "CN=OpenSignature Expired Allowed",
            notBefore: DateTimeOffset.UtcNow.AddYears(-2),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1),
            password: TestPassword);

        await using var provider = new PfxSigningProvider(
            new PfxSigningProviderOptions
            {
                ProviderId = "pfx-expired-allowed",
                Name = "PFX Expired Allowed",
                CertificateBytes = material.PfxBytes,
                Password = TestPassword
            },
            signingOptions: new SigningOptions { AllowExpiredCertificates = true });

        var certificates = await provider.ListCertificatesAsync();
        Assert.Single(certificates);
        Assert.True(certificates[0].CanSign);
        Assert.False(certificates[0].IsCurrentlyValid());

        var digest = RandomDigest(DigestAlgorithm.Sha256);
        var signature = await provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByThumbprint(certificates[0].Thumbprint));

        Assert.NotEmpty(signature);
        Assert.True(VerifyRsaSignature(certificates[0], digest, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task Missing_private_key_is_rejected()
    {
        using var material = EphemeralPfx.CreatePublicOnlyRsaPfx(
            subject: "CN=OpenSignature PublicOnly",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            password: TestPassword);

        await using var provider = CreateProvider(material.PfxBytes, TestPassword);

        var certificates = await provider.ListCertificatesAsync();
        Assert.Single(certificates);
        Assert.False(certificates[0].CanSign);
        Assert.True(certificates[0].IsCurrentlyValid());

        var digest = RandomDigest(DigestAlgorithm.Sha256);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByProviderReference(certificates[0].ProviderReference)));
    }

    [Fact]
    public async Task Loads_from_temp_path_and_deletes_file_after_test()
    {
        using var material = EphemeralPfx.CreateRsa(
            subject: "CN=OpenSignature Path",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            password: TestPassword);

        var tempPath = Path.Combine(Path.GetTempPath(), $"opensignature-pfx-{Guid.NewGuid():N}.pfx");
        try
        {
            await File.WriteAllBytesAsync(tempPath, material.PfxBytes);

            await using var provider = new PfxSigningProvider(new PfxSigningProviderOptions
            {
                ProviderId = "pfx-path",
                Name = "PFX Path Provider",
                Path = tempPath,
                Password = TestPassword
            });

            var certificates = await provider.ListCertificatesAsync();
            Assert.Single(certificates);
            Assert.True(certificates[0].CanSign);

            var health = await provider.GetHealthAsync();
            Assert.True(health.IsHealthy);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task Password_secret_name_resolves_via_secret_provider_stub()
    {
        using var material = EphemeralPfx.CreateRsa(
            subject: "CN=OpenSignature Secret",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            password: TestPassword);

        var secrets = new InMemorySigningSecretProvider(
            new Dictionary<string, string> { ["Signing:Pfx:Password"] = TestPassword });

        await using var provider = new PfxSigningProvider(
            new PfxSigningProviderOptions
            {
                ProviderId = "pfx-secret",
                Name = "PFX Secret Provider",
                CertificateBytes = material.PfxBytes,
                PasswordSecretName = "Signing:Pfx:Password"
            },
            secrets);

        var health = await provider.GetHealthAsync();
        Assert.True(health.IsHealthy);
    }

    [Fact]
    public async Task Ecdsa_certificate_signs_digest()
    {
        using var material = EphemeralPfx.CreateEcdsa(
            subject: "CN=OpenSignature ECDSA",
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddYears(1),
            password: TestPassword);

        await using var provider = CreateProvider(material.PfxBytes, TestPassword);
        var certificates = await provider.ListCertificatesAsync();
        Assert.Equal("ECDSA", certificates[0].PublicKeyAlgorithm);

        var digest = RandomDigest(DigestAlgorithm.Sha256);
        var signature = await provider.SignDigestAsync(
            digest,
            DigestAlgorithm.Sha256,
            SigningCertificateSelector.ByThumbprint(certificates[0].Thumbprint));

        Assert.True(VerifyEcdsaSignature(certificates[0], digest, signature));
    }

    private static PfxSigningProvider CreateProvider(byte[] pfxBytes, string password) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-test",
            Name = "PFX Test Provider",
            CertificateBytes = pfxBytes,
            Password = password
        });

    private static byte[] RandomDigest(DigestAlgorithm algorithm)
    {
        var digest = new byte[algorithm.GetDigestLengthBytes()];
        RandomNumberGenerator.Fill(digest);
        return digest;
    }

    private static bool VerifyRsaSignature(
        CertificateInfo certificate,
        byte[] digest,
        byte[] signature,
        HashAlgorithmName hashAlgorithm)
    {
        using var publicCert = X509CertificateLoader.LoadCertificate([.. certificate.PublicCertificateDer]);
        using var rsa = publicCert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("Expected RSA public key.");
        return rsa.VerifyHash(digest, signature, hashAlgorithm, RSASignaturePadding.Pkcs1);
    }

    private static bool VerifyEcdsaSignature(CertificateInfo certificate, byte[] digest, byte[] signature)
    {
        using var publicCert = X509CertificateLoader.LoadCertificate([.. certificate.PublicCertificateDer]);
        using var ecdsa = publicCert.GetECDsaPublicKey()
            ?? throw new InvalidOperationException("Expected ECDSA public key.");
        return ecdsa.VerifyHash(digest, signature);
    }

    /// <summary>
    /// Generates ephemeral PKCS#12 material in memory for tests. Never written to the repo.
    /// </summary>
    private sealed class EphemeralPfx : IDisposable
    {
        private EphemeralPfx(byte[] pfxBytes, X509Certificate2 certificate)
        {
            PfxBytes = pfxBytes;
            Certificate = certificate;
        }

        public byte[] PfxBytes { get; }

        public X509Certificate2 Certificate { get; }

        public static EphemeralPfx CreateRsa(
            string subject,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            string password)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
            var certificate = request.CreateSelfSigned(notBefore, notAfter);
            var pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
            return new EphemeralPfx(pfxBytes, certificate);
        }

        public static EphemeralPfx CreateEcdsa(
            string subject,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            string password)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            var certificate = request.CreateSelfSigned(notBefore, notAfter);
            var pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
            return new EphemeralPfx(pfxBytes, certificate);
        }

        public static EphemeralPfx CreatePublicOnlyRsaPfx(
            string subject,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            string password)
        {
            using var withKey = CreateRsa(subject, notBefore, notAfter, password);
            var publicDer = withKey.Certificate.Export(X509ContentType.Cert);
            using var publicOnly = X509CertificateLoader.LoadCertificate(publicDer);
            var pfxBytes = publicOnly.Export(X509ContentType.Pkcs12, password);
            return new EphemeralPfx(pfxBytes, X509CertificateLoader.LoadCertificate(publicDer));
        }

        public void Dispose() => Certificate.Dispose();
    }
}
