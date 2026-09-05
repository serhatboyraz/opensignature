using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;

namespace OpenSignature.Signing.Formats.Cades;

/// <summary>
/// CAdES Baseline B signer built on CMS SignedData with signing-time and ESS signing-certificate-v2.
/// Cryptographic signing is delegated to <see cref="ISigningProvider.SignDigestAsync"/>.
/// </summary>
public sealed class CadesBaselineBSigner : ICadesBaselineBSigner
{
    public async Task<CadesSignatureResult> SignAsync(
        byte[] content,
        CadesPackaging packaging,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificateSelector);
        cancellationToken.ThrowIfCancellationRequested();

        var detached = packaging == CadesPackaging.Detached;
        var cms = await CmsSignatureHelper.CreateSignedCmsAsync(
            content,
            detached,
            provider,
            certificateSelector,
            digestAlgorithm,
            cancellationToken).ConfigureAwait(false);

        var contentType = detached
            ? "application/pkcs7-signature"
            : "application/pkcs7-mime";

        return new CadesSignatureResult(cms, packaging, contentType);
    }
}
