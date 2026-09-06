using System.IO.Compression;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Cades;

namespace OpenSignature.Signing.Formats.Asic;

/// <summary>Creates ASiC-E (Associated Signature Container Extended) ZIP packages.</summary>
public interface IAsicESigner
{
    /// <summary>
    /// Builds an ASiC-E container with an ASiCManifest.xml and a detached CAdES-B over the manifest.
    /// A ZIP input is expanded into multiple data objects; any other payload is a single data object.
    /// </summary>
    Task<AsicSignatureResult> SignAsync(
        byte[] content,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        string? dataEntryName = null,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default,
        Func<byte[], CancellationToken, Task<byte[]>>? enhanceCmsAsync = null);
}

/// <summary>
/// ASiC-E signer per ETSI EN 319 162-1: one or more data objects, ASiCManifest.xml, CAdES over the manifest.
/// </summary>
public sealed class AsicESigner : IAsicESigner
{
    private static readonly byte[] ZipLocalHeaderMagic = [0x50, 0x4B, 0x03, 0x04];

    private readonly ICadesBaselineBSigner _cadesSigner;

    public AsicESigner(ICadesBaselineBSigner cadesSigner)
    {
        _cadesSigner = cadesSigner ?? throw new ArgumentNullException(nameof(cadesSigner));
    }

    public AsicESigner()
        : this(new CadesBaselineBSigner())
    {
    }

    public async Task<AsicSignatureResult> SignAsync(
        byte[] content,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        string? dataEntryName = null,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default,
        Func<byte[], CancellationToken, Task<byte[]>>? enhanceCmsAsync = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificateSelector);
        cancellationToken.ThrowIfCancellationRequested();

        var dataObjects = ExtractDataObjects(content, dataEntryName);
        var manifest = AsicManifestBuilder.CreateManifest(
            dataObjects,
            AsicConstants.CadesSignatureEntryName,
            AsicConstants.CadesSignatureMimeType,
            digestAlgorithm);

        var cades = await _cadesSigner.SignAsync(
            manifest,
            CadesPackaging.Detached,
            provider,
            certificateSelector,
            digestAlgorithm,
            cancellationToken).ConfigureAwait(false);

        var cms = cades.CmsBytes;
        if (enhanceCmsAsync is not null)
        {
            cms = await enhanceCmsAsync(cms, cancellationToken).ConfigureAwait(false);
        }

        var entries = new List<AsicZipEntry>(dataObjects.Count + 2);
        foreach (var dataObject in dataObjects)
        {
            entries.Add(new AsicZipEntry(dataObject.Uri, dataObject.Data));
        }

        entries.Add(new AsicZipEntry(AsicConstants.AsicManifestEntryName, manifest));
        entries.Add(new AsicZipEntry(AsicConstants.CadesSignatureEntryName, cms));

        var zip = AsicZip.Create(AsicConstants.AsicEMimeType, entries);
        return new AsicSignatureResult(zip, AsicConstants.AsicEContentType, dataObjects[0].Uri);
    }

    /// <summary>
    /// Validates an ASiC-E container: MIME type, manifest digests, and detached CAdES over the manifest.
    /// </summary>
    public static void Validate(byte[] containerBytes)
    {
        ArgumentNullException.ThrowIfNull(containerBytes);
        var (manifest, cms, files) = ExtractSignedPayload(containerBytes);
        AsicManifestBuilder.ValidateManifestDigests(manifest, files);
        CmsSignatureHelper.ValidateSignedCms(cms, manifest);
    }

    /// <summary>Extracts the manifest, CAdES bytes, and data objects from an ASiC-E container.</summary>
    public static (byte[] Manifest, byte[] Cms, IReadOnlyDictionary<string, byte[]> Files) ExtractSignedPayload(
        byte[] containerBytes)
    {
        ArgumentNullException.ThrowIfNull(containerBytes);

        if (!AsicZip.HasLeadingMimeTypeEntry(containerBytes, AsicConstants.AsicEMimeType))
        {
            throw new InvalidOperationException("ASiC-E container is missing a leading uncompressed mimetype entry.");
        }

        var entries = AsicZip.Read(containerBytes);
        var mime = entries.FirstOrDefault(static e =>
            e.Name.Equals(AsicConstants.MimeTypeEntryName, StringComparison.OrdinalIgnoreCase));
        if (mime is null
            || !System.Text.Encoding.ASCII.GetString(mime.Data).Equals(AsicConstants.AsicEMimeType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ZIP is not an ASiC-E container (mimetype mismatch).");
        }

        var manifest = entries.FirstOrDefault(static e =>
            e.Name.Equals(AsicConstants.AsicManifestEntryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ASiC-E container is missing META-INF/ASiCManifest.xml.");

        var signature = entries.FirstOrDefault(static e =>
            e.Name.Equals(AsicConstants.CadesSignatureEntryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ASiC-E container is missing META-INF/signature.p7s.");

        var files = entries
            .Where(static e => AsicSSigner.IsDataObjectEntry(e.Name))
            .ToDictionary(static e => e.Name, static e => e.Data, StringComparer.Ordinal);

        if (files.Count == 0)
        {
            throw new InvalidOperationException("ASiC-E container contains no data objects.");
        }

        return (manifest.Data, signature.Data, files);
    }

    private static IReadOnlyList<AsicDataObject> ExtractDataObjects(byte[] content, string? dataEntryName)
    {
        if (LooksLikeZip(content) && TryReadZipDataObjects(content, out var fromZip) && fromZip.Count > 0)
        {
            return fromZip;
        }

        var name = AsicZip.SanitizeDataEntryName(dataEntryName);
        return [new AsicDataObject(name, content)];
    }

    private static bool LooksLikeZip(byte[] content) =>
        content.Length >= 4 && content.AsSpan(0, 4).SequenceEqual(ZipLocalHeaderMagic);

    private static bool TryReadZipDataObjects(byte[] content, out List<AsicDataObject> objects)
    {
        objects = [];
        try
        {
            if (AsicZip.HasLeadingMimeTypeEntry(content, AsicConstants.AsicSMimeType)
                || AsicZip.HasLeadingMimeTypeEntry(content, AsicConstants.AsicEMimeType))
            {
                return false;
            }

            using var input = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(entry.Name) && name.EndsWith('/'))
                {
                    continue;
                }

                if (!AsicSSigner.IsDataObjectEntry(name))
                {
                    continue;
                }

                var safeName = AsicZip.SanitizeDataEntryName(name);
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                objects.Add(new AsicDataObject(safeName, buffer.ToArray()));
            }

            return objects.Count > 0;
        }
        catch (InvalidDataException)
        {
            objects = [];
            return false;
        }
    }
}
