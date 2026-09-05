using OpenSignature.Signing.Contracts;

namespace OpenSignature.Signing.Formats.Xades;

/// <summary>XAdES / XMLDSig packaging mode for Baseline B.</summary>
public enum XadesPackaging
{
    /// <summary>Signature element is inserted into the signed document element.</summary>
    Enveloped = 0,

    /// <summary>Signed data is placed inside a ds:Object referenced by the signature.</summary>
    Enveloping = 1,

    /// <summary>Signature references external content (content hash bound via Reference URI).</summary>
    Detached = 2
}

/// <summary>Creates XAdES Baseline B-like XML signatures (XMLDSig + qualifying properties).</summary>
public interface IXadesBaselineBSigner
{
    /// <summary>
    /// Signs XML <paramref name="xmlUtf8"/> producing a verifiable XML signature document.
    /// For <see cref="XadesPackaging.Detached"/>, returns a signature XML that references
    /// <c>#OpenSignature-Detached-Content</c> and embeds the content in a sibling object for
    /// self-contained verification in tests (see SIGNATURE-PROFILES.md).
    /// </summary>
    Task<XadesSignatureResult> SignAsync(
        byte[] xmlUtf8,
        XadesPackaging packaging,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an XAdES-B signature operation.</summary>
public sealed record XadesSignatureResult(byte[] SignedXmlUtf8, XadesPackaging Packaging, string ContentType);
