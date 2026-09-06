using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Cades;

namespace OpenSignature.Signing.Formats.Asic;

/// <summary>Creates ASiC-S (Associated Signature Container Simple) ZIP packages.</summary>
public interface IAsicSSigner
{
    /// <summary>
    /// Wraps a single data object and a detached CAdES-B signature in an ASiC-S container
    /// (<c>mimetype</c>, data file, <c>META-INF/signature.p7s</c>).
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

/// <summary>Result of an ASiC signature operation.</summary>
public sealed record AsicSignatureResult(byte[] ContainerBytes, string ContentType, string DataEntryName);

/// <summary>
/// ASiC-S signer per ETSI EN 319 162-1: one data object + associated CAdES detached signature.
/// </summary>
public sealed class AsicSSigner : IAsicSSigner
{
    private readonly ICadesBaselineBSigner _cadesSigner;

    public AsicSSigner(ICadesBaselineBSigner cadesSigner)
    {
        _cadesSigner = cadesSigner ?? throw new ArgumentNullException(nameof(cadesSigner));
    }

    public AsicSSigner()
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

        var entryName = AsicZip.SanitizeDataEntryName(dataEntryName);
        var cades = await _cadesSigner.SignAsync(
            content,
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

        var zip = AsicZip.Create(
            AsicConstants.AsicSMimeType,
            [
                new AsicZipEntry(entryName, content),
                new AsicZipEntry(AsicConstants.CadesSignatureEntryName, cms)
            ]);

        return new AsicSignatureResult(zip, AsicConstants.AsicSContentType, entryName);
    }

    /// <summary>
    /// Validates an ASiC-S container: MIME type, single data object, and detached CAdES over that object.
    /// </summary>
    public static void Validate(byte[] containerBytes)
    {
        ArgumentNullException.ThrowIfNull(containerBytes);

        if (!AsicZip.HasLeadingMimeTypeEntry(containerBytes, AsicConstants.AsicSMimeType))
        {
            throw new InvalidOperationException("ASiC-S container is missing a leading uncompressed mimetype entry.");
        }

        var (data, cms) = ExtractSignedPayload(containerBytes);
        CmsSignatureHelper.ValidateSignedCms(cms, data);
    }

    /// <summary>Extracts the signed data object and CAdES bytes from an ASiC-S container.</summary>
    public static (byte[] Data, byte[] Cms) ExtractSignedPayload(byte[] containerBytes)
    {
        ArgumentNullException.ThrowIfNull(containerBytes);
        var entries = AsicZip.Read(containerBytes);
        var mime = entries.FirstOrDefault(static e =>
            e.Name.Equals(AsicConstants.MimeTypeEntryName, StringComparison.OrdinalIgnoreCase));
        if (mime is null
            || !System.Text.Encoding.ASCII.GetString(mime.Data).Equals(AsicConstants.AsicSMimeType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ZIP is not an ASiC-S container (mimetype mismatch).");
        }

        var dataEntries = entries
            .Where(static e => IsDataObjectEntry(e.Name))
            .ToList();
        if (dataEntries.Count != 1)
        {
            throw new InvalidOperationException(
                $"ASiC-S requires exactly one data object; found {dataEntries.Count}.");
        }

        var signature = entries.FirstOrDefault(static e =>
            e.Name.Equals(AsicConstants.CadesSignatureEntryName, StringComparison.OrdinalIgnoreCase));
        if (signature is null)
        {
            throw new InvalidOperationException("ASiC-S container is missing META-INF/signature.p7s.");
        }

        return (dataEntries[0].Data, signature.Data);
    }

    internal static bool IsDataObjectEntry(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = name.Replace('\\', '/');
        if (normalized.Equals(AsicConstants.MimeTypeEntryName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !normalized.StartsWith(AsicConstants.MetaInfFolder, StringComparison.OrdinalIgnoreCase);
    }
}
