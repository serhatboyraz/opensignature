using OpenSignature.Domain.Enums;

namespace OpenSignature.Application.Abstractions.Signing;

/// <summary>
/// Result of <see cref="ISignatureCreationService.SignAsync"/>.
/// The caller owns disposal of <see cref="Content"/>.
/// </summary>
/// <param name="Content">Signed document stream (seekable when practical).</param>
/// <param name="ContentType">Suggested MIME type for the signed output.</param>
/// <param name="Format">Format that was applied (never silently downgraded).</param>
/// <param name="Profile">Profile that was applied (never silently downgraded).</param>
/// <param name="ProviderId">Resolved signing provider id.</param>
/// <param name="CertificateThumbprint">Thumbprint of the signing certificate.</param>
public sealed record SignatureCreationResult(
    Stream Content,
    string ContentType,
    SignatureFormat Format,
    SignatureProfile Profile,
    string ProviderId,
    string CertificateThumbprint);
