using System.Text;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Profiles;
using OpenSignature.Signing.Timestamping;

namespace OpenSignature.Signing.Tests;

public sealed class XadesAndPadesAdvancedProfileTests
{
    private const string Password = "unit-test-only-password";

    [Fact]
    public async Task Xades_t_lt_lta_add_unsigned_properties_and_remain_verifiable()
    {
        using var pki = EphemeralPki.Create(Password);
        await using var provider = CreateProvider(pki, "pfx-xades-adv");
        var selector = await SelectorAsync(provider);
        var signed = await new XadesBaselineBSigner().SignAsync(
            Encoding.UTF8.GetBytes("""<Doc>advanced</Doc>"""),
            XadesPackaging.Enveloped,
            provider,
            selector);

        using var tsa = new LocalRfc3161TimestampAuthority(
            new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var enhancer = new XadesProfileEnhancer(tsa, CreateLtv(pki));
        var signingDer = pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert);

        var t = await enhancer.ApplyAsync(signed.SignedXmlUtf8, SignatureProfile.T, signingDer);
        Assert.True(XadesProfileEnhancer.HasSignatureTimeStamp(t));
        Assert.True(XadesBaselineBSigner.CheckSignature(t));

        var lt = await enhancer.ApplyAsync(signed.SignedXmlUtf8, SignatureProfile.LT, signingDer);
        Assert.True(XadesProfileEnhancer.HasCertificateValues(lt));
        Assert.True(XadesProfileEnhancer.HasRevocationValues(lt));
        Assert.True(XadesBaselineBSigner.CheckSignature(lt));

        var lta = await enhancer.ApplyAsync(signed.SignedXmlUtf8, SignatureProfile.LTA, signingDer);
        Assert.True(XadesProfileEnhancer.HasArchiveTimeStamp(lta));
        Assert.True(XadesBaselineBSigner.CheckSignature(lta));
    }

    [Fact]
    public async Task Pades_t_lt_lta_keep_byte_range_valid()
    {
        using var pki = EphemeralPki.Create(Password);
        await using var provider = CreateProvider(pki, "pfx-pades-adv");
        var selector = await SelectorAsync(provider);
        using var tsa = new LocalRfc3161TimestampAuthority(
            new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var cadesEnhancer = new CadesProfileEnhancer(tsa, CreateLtv(pki));
        var ltv = CreateLtv(pki);
        var signingDer = pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert);
        var updater = new PadesLongTermUpdater(tsa);
        var signer = new PadesBaselineBSigner();

        var t = await signer.SignAsync(
            PadesBaselineBSigner.CreateMinimalPdf(),
            provider,
            selector,
            contentsHexLength: PadesBaselineBSigner.AdvancedProfileContentsHexLength,
            enhanceCmsAsync: (cms, ct) => cadesEnhancer.ApplyAsync(cms, SignatureProfile.T, signingDer, cancellationToken: ct));
        PadesBaselineBSigner.ValidateSignedPdf(t.SignedPdf);
        Assert.True(CadesProfileEnhancer.HasSignatureTimestamp(
            OpenSignature.Signing.Crypto.PdfByteRangeHelper.ExtractCmsFromContents(t.SignedPdf)));

        var ltPdf = await signer.SignAsync(
            PadesBaselineBSigner.CreateMinimalPdf(),
            provider,
            selector,
            contentsHexLength: PadesBaselineBSigner.AdvancedProfileContentsHexLength,
            enhanceCmsAsync: (cms, ct) => cadesEnhancer.ApplyAsync(cms, SignatureProfile.LT, signingDer, cancellationToken: ct));
        var withDss = updater.AppendDss(ltPdf.SignedPdf, await ltv.CollectAsync(signingDer));
        PadesBaselineBSigner.ValidateSignedPdf(withDss);
        Assert.True(PadesLongTermUpdater.HasDss(withDss));

        var lta = await updater.AppendDocumentTimestampAsync(withDss);
        PadesBaselineBSigner.ValidateSignedPdf(lta);
        Assert.True(PadesLongTermUpdater.HasDocumentTimestamp(lta));
    }

    private static StaticLongTermValidationDataProvider CreateLtv(EphemeralPki pki) =>
        new(new LongTermValidationMaterial(
            [pki.RootCaDer, pki.SigningCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert)],
            [pki.CrlDer],
            []));

    private static PfxSigningProvider CreateProvider(EphemeralPki pki, string providerId) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = providerId,
            Name = "Advanced PFX",
            CertificateBytes = pki.SigningPfxBytes,
            Password = Password
        });

    private static async Task<SigningCertificateSelector> SelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
