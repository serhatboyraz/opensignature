using OpenSignature.Domain.Enums;
using OpenSignature.Validation.Certificates;

namespace OpenSignature.Validation.Signatures;

/// <summary>Input for signature validation.</summary>
public sealed class SignatureValidationRequest
{
    public SignatureValidationRequest(
        SignatureFormat format,
        byte[] signedBytes,
        byte[]? originalContent = null,
        CertificateValidationOptions? certificateOptions = null,
        string? expectedSignerThumbprint = null)
    {
        ArgumentNullException.ThrowIfNull(signedBytes);

        Format = format;
        SignedBytes = signedBytes;
        OriginalContent = originalContent;
        CertificateOptions = certificateOptions;
        ExpectedSignerThumbprint = string.IsNullOrWhiteSpace(expectedSignerThumbprint)
            ? null
            : expectedSignerThumbprint.Trim();
    }

    public SignatureFormat Format { get; }

    /// <summary>Signed artifact (CMS, signed XML, or signed PDF).</summary>
    public byte[] SignedBytes { get; }

    /// <summary>
    /// Original content for detached CAdES. Ignored for attached CAdES, XAdES enveloped, and PAdES.
    /// </summary>
    public byte[]? OriginalContent { get; }

    public CertificateValidationOptions? CertificateOptions { get; }

    /// <summary>When set, signer certificate thumbprint must match (case-insensitive hex).</summary>
    public string? ExpectedSignerThumbprint { get; }
}
