using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;

namespace OpenSignature.Signing.Orchestration;

/// <summary>
/// Resolves format/profile/provider and creates signatures using format-specific Baseline B signers.
/// Does not persist files or update job state — callers own storage and lifecycle.
/// </summary>
public sealed class SignatureOrchestrator : ISignatureCreationService
{
    private readonly ISigningProviderResolver _providerResolver;
    private readonly ICadesBaselineBSigner _cadesSigner;
    private readonly IXadesBaselineBSigner _xadesSigner;
    private readonly IPadesBaselineBSigner _padesSigner;

    public SignatureOrchestrator(
        ISigningProviderResolver providerResolver,
        ICadesBaselineBSigner cadesSigner,
        IXadesBaselineBSigner xadesSigner,
        IPadesBaselineBSigner padesSigner)
    {
        _providerResolver = providerResolver ?? throw new ArgumentNullException(nameof(providerResolver));
        _cadesSigner = cadesSigner ?? throw new ArgumentNullException(nameof(cadesSigner));
        _xadesSigner = xadesSigner ?? throw new ArgumentNullException(nameof(xadesSigner));
        _padesSigner = padesSigner ?? throw new ArgumentNullException(nameof(padesSigner));
    }

    /// <inheritdoc />
    public async Task<SignatureCreationResult> SignAsync(
        Stream inputStream,
        SignatureFormat format,
        SignatureProfile profile,
        SigningProviderType providerType,
        SigningCertificateSelector? certificateSelector,
        CancellationToken cancellationToken = default,
        SignatureAppearanceOptions? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        cancellationToken.ThrowIfCancellationRequested();

        if (appearance is { Visible: true } && format != SignatureFormat.PAdES)
        {
            throw new InvalidOperationException(
                "Visible signature appearance is only supported for PAdES.");
        }

        if (profile != SignatureProfile.B)
        {
            throw new UnsupportedSignatureProfileException(
                $"Signature profile '{profile}' is not supported. OpenSignature currently implements Baseline B only; profiles are never silently downgraded.",
                profile);
        }

        if (format is SignatureFormat.ASiC_S or SignatureFormat.ASiC_E)
        {
            throw new UnsupportedSignatureFormatException(
                $"Signature format '{format}' is not supported yet.",
                format);
        }

        var provider = _providerResolver.Resolve(providerType);
        var selector = certificateSelector
            ?? await ResolveDefaultCertificateSelectorAsync(provider, cancellationToken).ConfigureAwait(false);

        await using var buffer = new MemoryStream();
        await inputStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var input = buffer.ToArray();

        return format switch
        {
            SignatureFormat.CAdES => await SignCadesAsync(input, provider, selector, cancellationToken).ConfigureAwait(false),
            SignatureFormat.XAdES => await SignXadesAsync(input, provider, selector, cancellationToken).ConfigureAwait(false),
            SignatureFormat.PAdES => await SignPadesAsync(input, provider, selector, appearance, cancellationToken).ConfigureAwait(false),
            _ => throw new UnsupportedSignatureFormatException(
                $"Signature format '{format}' is not supported.",
                format)
        };
    }

    private async Task<SignatureCreationResult> SignCadesAsync(
        byte[] input,
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        var result = await _cadesSigner.SignAsync(
            input,
            CadesPackaging.Attached,
            provider,
            selector,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(result.CmsBytes, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.CAdES,
            Profile: SignatureProfile.B,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private async Task<SignatureCreationResult> SignXadesAsync(
        byte[] input,
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        var result = await _xadesSigner.SignAsync(
            input,
            XadesPackaging.Enveloped,
            provider,
            selector,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(result.SignedXmlUtf8, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.XAdES,
            Profile: SignatureProfile.B,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private async Task<SignatureCreationResult> SignPadesAsync(
        byte[] input,
        ISigningProvider provider,
        SigningCertificateSelector selector,
        SignatureAppearanceOptions? appearance,
        CancellationToken cancellationToken)
    {
        PadesVisibleAppearance? padesAppearance = null;
        if (appearance is { Visible: true })
        {
            padesAppearance = new PadesVisibleAppearance(
                appearance.Note,
                appearance.ImageBytes,
                appearance.ImageContentType,
                appearance.PageNumber < 1 ? 1 : appearance.PageNumber);
        }

        var result = await _padesSigner.SignAsync(
            input,
            provider,
            selector,
            appearance: padesAppearance,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(result.SignedPdf, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.PAdES,
            Profile: SignatureProfile.B,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private static async Task<SigningCertificateSelector> ResolveDefaultCertificateSelectorAsync(
        ISigningProvider provider,
        CancellationToken cancellationToken)
    {
        var certificates = await provider.ListCertificatesAsync(cancellationToken).ConfigureAwait(false);
        var signable = certificates.FirstOrDefault(static c => c.CanSign)
            ?? throw new InvalidOperationException(
                $"Signing provider '{provider.ProviderId}' has no signable certificate to use as a default.");

        return SigningCertificateSelector.ByThumbprint(signable.Thumbprint);
    }
}
