using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Asic;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class AsicSSignerTests
{
    private const string TestPassword = "unit-test-only-password";

    [Fact]
    public async Task Asic_s_container_validates_and_tampered_data_fails()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("OpenSignature ASiC-S payload");

        var signer = new AsicSSigner();
        var result = await signer.SignAsync(content, provider, selector, dataEntryName: "contract.txt");

        Assert.Equal(AsicConstants.AsicSContentType, result.ContentType);
        Assert.Equal("contract.txt", result.DataEntryName);
        Assert.True(AsicZip.HasLeadingMimeTypeEntry(result.ContainerBytes, AsicConstants.AsicSMimeType));

        AsicSSigner.Validate(result.ContainerBytes);

        var (data, cms) = AsicSSigner.ExtractSignedPayload(result.ContainerBytes);
        Assert.Equal(content, data);
        Assert.NotEmpty(cms);

        var entries = AsicZip.Read(result.ContainerBytes);
        var dataEntry = entries.Single(e => e.Name == "contract.txt");
        dataEntry.Data[0] ^= 0xFF;
        var tampered = AsicZip.Create(
            AsicConstants.AsicSMimeType,
            [
                new AsicZipEntry("contract.txt", dataEntry.Data),
                new AsicZipEntry(AsicConstants.CadesSignatureEntryName, cms)
            ]);

        Assert.ThrowsAny<Exception>(() => AsicSSigner.Validate(tampered));
    }

    [Fact]
    public void Sanitize_rejects_traversal_and_reserved_names()
    {
        Assert.Equal("document.bin", AsicZip.SanitizeDataEntryName("../secret.txt"));
        Assert.Equal("document.bin", AsicZip.SanitizeDataEntryName("mimetype"));
        Assert.Equal("document.bin", AsicZip.SanitizeDataEntryName("META-INF/x"));
        Assert.Equal("ok.pdf", AsicZip.SanitizeDataEntryName(@"C:\uploads\ok.pdf"));
    }

    private static EphemeralPfx CreateMaterial() =>
        EphemeralPfx.CreateRsa(
            "CN=OpenSignature ASiC-S",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

    private static PfxSigningProvider CreateProvider(EphemeralPfx material) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-asic-s",
            Name = "ASiC-S Test",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

    private static async Task<SigningCertificateSelector> DefaultSelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
