using System.Text;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Pfx;
using OpenSignature.Validation.Certificates;
using OpenSignature.Validation.Revocation;
using OpenSignature.Validation.Signatures;

namespace OpenSignature.Validation.Tests;

public sealed class SignatureValidatorTests
{
    private const string Password = "validation-test-only-password";

    [Fact]
    public async Task Cades_detached_valid_signature()
    {
        using var material = EphemeralPfx.CreateRsa("CN=CAdES Valid", Password);
        await using var provider = CreateProvider(material, "pfx-cades-valid");
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("cades-valid");

        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Detached, provider, selector);

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.CAdES, signed.CmsBytes, content));

        Assert.True(result.IsValid);
        Assert.True(result.CryptoValid);
        Assert.Contains(SignatureValidationCodes.SigValid, result.ReasonCodes);
    }

    [Fact]
    public async Task Cades_modified_document_fails()
    {
        using var material = EphemeralPfx.CreateRsa("CN=CAdES Modified", Password);
        await using var provider = CreateProvider(material, "pfx-cades-mod");
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("original");

        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Detached, provider, selector);

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(
                SignatureFormat.CAdES,
                signed.CmsBytes,
                Encoding.UTF8.GetBytes("tampered")));

        Assert.False(result.IsValid);
        Assert.False(result.CryptoValid);
        Assert.Contains(SignatureValidationCodes.SigCryptoInvalid, result.ReasonCodes);
    }

    [Fact]
    public async Task Cades_wrong_expected_thumbprint_fails()
    {
        using var material = EphemeralPfx.CreateRsa("CN=CAdES WrongCert", Password);
        await using var provider = CreateProvider(material, "pfx-cades-wrong");
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("wrong-cert");

        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Detached, provider, selector);

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(
                SignatureFormat.CAdES,
                signed.CmsBytes,
                content,
                expectedSignerThumbprint: new string('A', 40)));

        Assert.False(result.IsValid);
        Assert.Contains(SignatureValidationCodes.SigUnexpectedSigner, result.ReasonCodes);
    }

    [Fact]
    public async Task Xades_enveloped_valid_signature()
    {
        using var material = EphemeralPfx.CreateRsa("CN=XAdES Valid", Password);
        await using var provider = CreateProvider(material, "pfx-xades-valid");
        var selector = await SelectorAsync(provider);

        var signed = await new XadesBaselineBSigner()
            .SignAsync(Encoding.UTF8.GetBytes("""<Doc>xades</Doc>"""), XadesPackaging.Enveloped, provider, selector);

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.XAdES, signed.SignedXmlUtf8));

        Assert.True(result.IsValid);
        Assert.True(result.CryptoValid);
    }

    [Fact]
    public async Task Xades_modified_document_fails()
    {
        using var material = EphemeralPfx.CreateRsa("CN=XAdES Modified", Password);
        await using var provider = CreateProvider(material, "pfx-xades-mod");
        var selector = await SelectorAsync(provider);

        var signed = await new XadesBaselineBSigner()
            .SignAsync(Encoding.UTF8.GetBytes("""<Doc>xades</Doc>"""), XadesPackaging.Enveloped, provider, selector);

        var tampered = (byte[])signed.SignedXmlUtf8.Clone();
        // Corrupt a character inside the original document element payload (before Signature).
        var marker = Encoding.UTF8.GetBytes("<Doc>");
        var index = IndexOf(tampered, marker);
        Assert.True(index >= 0, "Expected <Doc> marker in signed XML.");
        tampered[index + marker.Length] ^= 0x20;

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.XAdES, tampered));

        Assert.False(result.IsValid);
        Assert.Contains(SignatureValidationCodes.SigCryptoInvalid, result.ReasonCodes);
    }

    [Fact]
    public async Task Pades_valid_signature()
    {
        using var material = EphemeralPfx.CreateRsa("CN=PAdES Valid", Password);
        await using var provider = CreateProvider(material, "pfx-pades-valid");
        var selector = await SelectorAsync(provider);

        var signed = await new PadesBaselineBSigner()
            .SignAsync(PadesBaselineBSigner.CreateMinimalPdf(), provider, selector);

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.PAdES, signed.SignedPdf));

        Assert.True(result.IsValid);
        Assert.True(result.CryptoValid);
    }

    [Fact]
    public async Task Pades_modified_pdf_fails()
    {
        using var material = EphemeralPfx.CreateRsa("CN=PAdES Modified", Password);
        await using var provider = CreateProvider(material, "pfx-pades-mod");
        var selector = await SelectorAsync(provider);

        var signed = await new PadesBaselineBSigner()
            .SignAsync(PadesBaselineBSigner.CreateMinimalPdf(), provider, selector);

        var tampered = (byte[])signed.SignedPdf.Clone();
        // Flip a byte in the first content region (before signature dictionary).
        tampered[20] ^= 0xFF;

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.PAdES, tampered));

        Assert.False(result.IsValid);
        Assert.False(result.CryptoValid);
    }

    [Fact]
    public async Task Unsupported_format_returns_SIG_FORMAT_UNSUPPORTED()
    {
        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(SignatureFormat.ASiC_S, [0x01]));

        Assert.False(result.IsValid);
        Assert.Contains(SignatureValidationCodes.SigFormatUnsupported, result.ReasonCodes);
    }

    [Fact]
    public async Task Cades_certificate_options_asof_after_expiry_fails_certificate_stage()
    {
        using var material = EphemeralPfx.CreateRsa("CN=AsOf Expiry", Password);
        await using var provider = CreateProvider(material, "pfx-asof");
        var selector = await SelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("asof");

        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Detached, provider, selector);

        var certOptions = new CertificateValidationOptions
        {
            UseCustomTrustStore = true,
            AllowSelfSignedWhenTrusted = true,
            RevocationMode = RevocationMode.Offline,
            AsOf = DateTimeOffset.UtcNow.AddYears(50)
        };
        certOptions.TrustAnchors.Add(material.Certificate);

        var result = await new SignatureValidator().ValidateAsync(
            new SignatureValidationRequest(
                SignatureFormat.CAdES,
                signed.CmsBytes,
                content,
                certificateOptions: certOptions));

        Assert.False(result.IsValid);
        Assert.True(result.CryptoValid);
        Assert.Contains(SignatureValidationCodes.SigCertificateInvalid, result.ReasonCodes);
        Assert.Contains(CertificateValidationCodes.CertExpired, result.ReasonCodes);
    }

    private static PfxSigningProvider CreateProvider(EphemeralPfx material, string providerId) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = providerId,
            Name = "Validation Test PFX",
            CertificateBytes = material.PfxBytes,
            Password = Password
        });

    private static async Task<SigningCertificateSelector> SelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
