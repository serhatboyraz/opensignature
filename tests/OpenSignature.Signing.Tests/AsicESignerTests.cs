using System.IO.Compression;
using System.Text;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Asic;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class AsicESignerTests
{
    private const string TestPassword = "unit-test-only-password";

    [Fact]
    public async Task Asic_e_single_file_manifest_validates_and_tamper_fails()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);
        var content = Encoding.UTF8.GetBytes("OpenSignature ASiC-E payload");

        var signer = new AsicESigner();
        var result = await signer.SignAsync(content, provider, selector, dataEntryName: "note.txt");

        Assert.Equal(AsicConstants.AsicEContentType, result.ContentType);
        Assert.True(AsicZip.HasLeadingMimeTypeEntry(result.ContainerBytes, AsicConstants.AsicEMimeType));
        AsicESigner.Validate(result.ContainerBytes);

        var (manifest, cms, files) = AsicESigner.ExtractSignedPayload(result.ContainerBytes);
        Assert.True(files.ContainsKey("note.txt"));
        Assert.NotEmpty(manifest);
        Assert.NotEmpty(cms);

        files["note.txt"][0] ^= 0xFF;
        var tampered = AsicZip.Create(
            AsicConstants.AsicEMimeType,
            [
                new AsicZipEntry("note.txt", files["note.txt"]),
                new AsicZipEntry(AsicConstants.AsicManifestEntryName, manifest),
                new AsicZipEntry(AsicConstants.CadesSignatureEntryName, cms)
            ]);

        Assert.ThrowsAny<Exception>(() => AsicESigner.Validate(tampered));
    }

    [Fact]
    public async Task Asic_e_zip_input_signs_each_entry_via_manifest()
    {
        using var material = CreateMaterial();
        await using var provider = CreateProvider(material);
        var selector = await DefaultSelectorAsync(provider);

        using var zipBuffer = new MemoryStream();
        using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "a.txt", "alpha");
            AddEntry(zip, "b.txt", "bravo");
        }

        var signer = new AsicESigner();
        var result = await signer.SignAsync(zipBuffer.ToArray(), provider, selector);
        var (_, _, files) = AsicESigner.ExtractSignedPayload(result.ContainerBytes);

        Assert.Equal(2, files.Count);
        Assert.True(files.ContainsKey("a.txt"));
        Assert.True(files.ContainsKey("b.txt"));
        AsicESigner.Validate(result.ContainerBytes);
    }

    private static void AddEntry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
    }

    private static EphemeralPfx CreateMaterial() =>
        EphemeralPfx.CreateRsa(
            "CN=OpenSignature ASiC-E",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

    private static PfxSigningProvider CreateProvider(EphemeralPfx material) =>
        new(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-asic-e",
            Name = "ASiC-E Test",
            CertificateBytes = material.PfxBytes,
            Password = TestPassword
        });

    private static async Task<SigningCertificateSelector> DefaultSelectorAsync(PfxSigningProvider provider)
    {
        var certs = await provider.ListCertificatesAsync();
        return SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
    }
}
