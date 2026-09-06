using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Asic;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Interop.Tests;

/// <summary>
/// Independent validation of signatures produced by OpenSignature format signers
/// using BCL SignedCms / SignedXml (and PAdES ByteRange + CMS checks).
/// </summary>
public sealed class SignatureInteropTests
{
    private const string TestPassword = "interop-test-only-password";

    [Fact]
    public async Task Cades_detached_interop_validation()
    {
        using var material = CreateMaterial("CN=Interop CAdES");
        await using var provider = CreateProvider(material, "pfx-interop-cades");
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("interop-cades");

        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Detached, provider, selector);

        CmsSignatureHelper.ValidateSignedCms(signed.CmsBytes, content);
    }

    [Fact]
    public async Task Xades_enveloped_interop_validation()
    {
        using var material = CreateMaterial("CN=Interop XAdES");
        await using var provider = CreateProvider(material, "pfx-interop-xades");
        var selector = await SelectorAsync(provider);

        var signed = await new XadesBaselineBSigner()
            .SignAsync(Encoding.UTF8.GetBytes("""<Doc>interop</Doc>"""), XadesPackaging.Enveloped, provider, selector);

        Assert.True(XadesBaselineBSigner.CheckSignature(signed.SignedXmlUtf8));
    }

    [Fact]
    public async Task Pades_byte_range_cms_interop_validation()
    {
        using var material = CreateMaterial("CN=Interop PAdES");
        await using var provider = CreateProvider(material, "pfx-interop-pades");
        var selector = await SelectorAsync(provider);

        var signed = await new PadesBaselineBSigner()
            .SignAsync(PadesBaselineBSigner.CreateMinimalPdf(), provider, selector);

        PadesBaselineBSigner.ValidateSignedPdf(signed.SignedPdf);
    }

    [Fact]
    public async Task Asic_s_and_asic_e_interop_validation()
    {
        using var material = CreateMaterial("CN=Interop ASiC");
        await using var provider = CreateProvider(material, "pfx-interop-asic");
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("interop-asic");

        var asicS = await new AsicSSigner().SignAsync(content, provider, selector);
        AsicSSigner.Validate(asicS.ContainerBytes);

        var asicE = await new AsicESigner().SignAsync(content, provider, selector);
        AsicESigner.Validate(asicE.ContainerBytes);
    }

    private static EphemeralPfx CreateMaterial(string subject) =>
        EphemeralPfx.CreateRsa(
            subject,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

    private static PfxSigningProvider CreateProvider(EphemeralPfx material, string providerId) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = providerId,
            Name = "Interop PFX",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

    private static async Task<SigningCertificateSelector> SelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }

    /// <summary>Local ephemeral PFX helper (mirrors Signing.Tests; never committed).</summary>
    private sealed class EphemeralPfx : IDisposable
    {
        private EphemeralPfx(byte[] pfxBytes, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
        {
            PfxBytes = pfxBytes;
            Certificate = certificate;
        }

        public byte[] PfxBytes { get; }

        public System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate { get; }

        public static EphemeralPfx CreateRsa(
            string subject,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            string password)
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                subject,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
                    System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature
                    | System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.NonRepudiation,
                    critical: true));
            var certificate = request.CreateSelfSigned(notBefore, notAfter);
            var pfxBytes = certificate.Export(
                System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12,
                password);
            return new EphemeralPfx(pfxBytes, certificate);
        }

        public void Dispose() => Certificate.Dispose();
    }
}
