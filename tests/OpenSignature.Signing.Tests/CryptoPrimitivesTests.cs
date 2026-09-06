using System.Security.Cryptography;
using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class CryptoPrimitivesTests
{
    private const string TestPassword = "unit-test-only-password";

    [Theory]
    [InlineData(DigestAlgorithm.Sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData(DigestAlgorithm.Sha384, "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7")]
    [InlineData(DigestAlgorithm.Sha512, "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    public void DigestHelper_matches_NIST_abc_vectors(DigestAlgorithm algorithm, string expectedHex)
    {
        var digest = DigestHelper.ComputeDigest(Encoding.ASCII.GetBytes("abc"), algorithm);
        Assert.Equal(expectedHex, Convert.ToHexString(digest).ToLowerInvariant());
    }

    [Fact]
    public void CertificateHelper_exports_public_der_and_validity()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature CertHelper",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        var der = CertificateHelper.ExportPublicDer(material.Certificate);
        Assert.NotEmpty(der);

        using var loaded = CertificateHelper.LoadPublic(der);
        Assert.Equal(material.Certificate.Thumbprint, loaded.Thumbprint);
        Assert.True(CertificateHelper.IsCurrentlyValid(loaded));
        Assert.False(loaded.HasPrivateKey);
    }

    [Fact]
    public async Task CmsSignatureHelper_creates_detached_and_attached_cms()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature CmsHelper",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        await using var provider = new PfxSigningProvider(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-cms",
            Name = "CMS Test",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

        var certs = await provider.ListCertificatesAsync();
        var selector = SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
        var content = Encoding.UTF8.GetBytes("OpenSignature CMS content");

        var detached = await CmsSignatureHelper.CreateSignedCmsAsync(
            content, detached: true, provider, selector);
        CmsSignatureHelper.ValidateSignedCms(detached, content);

        var attached = await CmsSignatureHelper.CreateSignedCmsAsync(
            content, detached: false, provider, selector);
        CmsSignatureHelper.ValidateSignedCms(attached);
        Assert.Equal(content, CmsSignatureHelper.TryGetEncapsulatedContent(attached));
    }

    [Fact]
    public async Task CmsSignatureHelper_signs_expired_certificate_when_provider_allows_it()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature CmsExpired",
            DateTimeOffset.UtcNow.AddYears(-2),
            DateTimeOffset.UtcNow.AddDays(-1),
            TestPassword);

        await using var provider = new PfxSigningProvider(
            new PfxSigningProviderOptions
            {
                ProviderId = "pfx-cms-expired",
                Name = "CMS Expired Test",
                CertificateBytes = material.PfxBytes,
                Password = TestPassword
            },
            signingOptions: new SigningOptions { AllowExpiredCertificates = true });

        var certs = await provider.ListCertificatesAsync();
        Assert.True(certs[0].CanSign);
        var selector = SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
        var content = Encoding.UTF8.GetBytes("expired-cms");

        var cms = await CmsSignatureHelper.CreateSignedCmsAsync(content, detached: true, provider, selector);
        CmsSignatureHelper.ValidateSignedCms(cms, content);
    }

    [Fact]
    public void XmlCanonicalizationHelper_exclusive_c14n_is_deterministic()
    {
        const string xml = """<Root xmlns:a="urn:a"><a:Child>value</a:Child></Root>""";
        var document = XmlCanonicalizationHelper.LoadXmlDocument(xml);
        var first = XmlCanonicalizationHelper.CanonicalizeExclusive(document.DocumentElement!);
        var second = XmlCanonicalizationHelper.CanonicalizeExclusive(document.DocumentElement!);
        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void PdfByteRangeHelper_digests_ranges()
    {
        var pdf = Encoding.ASCII.GetBytes("ABCDEFGHIJ");
        var digest = PdfByteRangeHelper.ComputeDigestOverByteRanges(
            pdf,
            [0, 4, 6, 4],
            DigestAlgorithm.Sha256);

        var expected = SHA256.HashData(Encoding.ASCII.GetBytes("ABCDGHIJ"));
        Assert.Equal(expected, digest);
    }
}
