using System.Text;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Orchestration;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Profiles;
using OpenSignature.Signing.Timestamping;

namespace OpenSignature.Signing.Tests;

public sealed class CadesAdvancedProfileTests
{
    private const string Password = "unit-test-only-password";

    [Fact]
    public async Task Cades_t_adds_signature_timestamp_and_still_validates()
    {
        using var pki = EphemeralPki.Create(Password);
        await using var provider = CreateProvider(pki);
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("cades-t-payload");

        var baseline = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Attached, provider, selector);
        using var tsa = new LocalRfc3161TimestampAuthority(
            new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var enhancer = new CadesProfileEnhancer(tsa, CreateLtv(pki));

        var enhanced = await enhancer.ApplyAsync(
            baseline.CmsBytes,
            SignatureProfile.T,
            pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));

        Assert.True(CadesProfileEnhancer.HasSignatureTimestamp(enhanced));
        Assert.False(CadesProfileEnhancer.HasCertificateValues(enhanced));
        CmsSignatureHelper.ValidateSignedCms(enhanced);
        Assert.NotNull(CadesProfileEnhancer.GetSignatureTimestamp(enhanced));
    }

    [Fact]
    public async Task Cades_lt_embeds_certificates_and_crl()
    {
        using var pki = EphemeralPki.Create(Password);
        await using var provider = CreateProvider(pki);
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("cades-lt-payload");

        var baseline = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Attached, provider, selector);
        using var tsa = new LocalRfc3161TimestampAuthority(
            new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var enhancer = new CadesProfileEnhancer(tsa, CreateLtv(pki));

        var enhanced = await enhancer.ApplyAsync(
            baseline.CmsBytes,
            SignatureProfile.LT,
            pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));

        Assert.True(CadesProfileEnhancer.HasSignatureTimestamp(enhanced));
        Assert.True(CadesProfileEnhancer.HasCertificateValues(enhanced));
        Assert.True(CadesProfileEnhancer.HasRevocationValues(enhanced));
        CmsSignatureHelper.ValidateSignedCms(enhanced);
    }

    [Fact]
    public async Task Cades_lta_adds_archive_timestamp()
    {
        using var pki = EphemeralPki.Create(Password);
        await using var provider = CreateProvider(pki);
        var selector = await SelectorAsync(provider);

        var baseline = await new CadesBaselineBSigner()
            .SignAsync("cades-lta"u8.ToArray(), CadesPackaging.Attached, provider, selector);
        using var tsa = new LocalRfc3161TimestampAuthority(
            new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var enhancer = new CadesProfileEnhancer(tsa, CreateLtv(pki));

        var enhanced = await enhancer.ApplyAsync(
            baseline.CmsBytes,
            SignatureProfile.LTA,
            pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));

        Assert.True(CadesProfileEnhancer.HasArchiveTimestamp(enhanced));
        CmsSignatureHelper.ValidateSignedCms(enhanced);
    }

    [Fact]
    public async Task Cades_lt_without_revocation_data_fails_without_downgrade()
    {
        using var pki = EphemeralPki.Create(Password);
        await using var provider = CreateProvider(pki);
        var selector = await SelectorAsync(provider);

        var baseline = await new CadesBaselineBSigner()
            .SignAsync("cades-lt-fail"u8.ToArray(), CadesPackaging.Attached, provider, selector);
        using var tsa = new LocalRfc3161TimestampAuthority(
            new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var enhancer = new CadesProfileEnhancer(tsa, new SigningCertificateOnlyValidationDataProvider());

        await Assert.ThrowsAsync<LongTermValidationDataUnavailableException>(() =>
            enhancer.ApplyAsync(
                baseline.CmsBytes,
                SignatureProfile.LT,
                pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert)));
    }

    private static StaticLongTermValidationDataProvider CreateLtv(EphemeralPki pki) =>
        new(new LongTermValidationMaterial(
            [pki.RootCaDer, pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert)],
            [pki.CrlDer],
            []));

    private static PfxSigningProvider CreateProvider(EphemeralPki pki) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-cades-adv",
            Name = "CAdES Advanced",
            CertificateBytes = pki.SigningPfxBytes,
            Password = Password
        });

    private static async Task<SigningCertificateSelector> SelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
