using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Formats.Cades;

/// <summary>CAdES packaging mode for Baseline B.</summary>
public enum CadesPackaging
{
    /// <summary>Detached CMS SignedData (signature does not encapsulate content).</summary>
    Detached = 0,

    /// <summary>Attached / encapsulated CMS SignedData (content embedded in SignedData).</summary>
    Attached = 1
}

/// <summary>Creates CAdES Baseline B (CMS SignedData with Baseline B signed attributes).</summary>
public interface ICadesBaselineBSigner
{
    /// <summary>
    /// Signs <paramref name="content"/> as CAdES-B using <paramref name="provider"/>.
    /// </summary>
    Task<CadesSignatureResult> SignAsync(
        byte[] content,
        CadesPackaging packaging,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a CAdES-B signature operation.</summary>
/// <param name="CmsBytes">DER-encoded CMS SignedData.</param>
/// <param name="Packaging">Packaging mode used.</param>
/// <param name="ContentType">Suggested MIME type.</param>
public sealed record CadesSignatureResult(
    byte[] CmsBytes,
    CadesPackaging Packaging,
    string ContentType);
