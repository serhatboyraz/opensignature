using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Asic;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Profiles;

namespace OpenSignature.Signing.Orchestration;

/// <summary>
/// Resolves format/profile/provider and creates signatures using format-specific signers.
/// Does not persist files or update job state — callers own storage and lifecycle.
/// Profiles are never silently downgraded.
/// </summary>
public sealed class SignatureOrchestrator : ISignatureCreationService
{
    private readonly ISigningProviderResolver _providerResolver;
    private readonly ICadesBaselineBSigner _cadesSigner;
    private readonly IXadesBaselineBSigner _xadesSigner;
    private readonly IPadesBaselineBSigner _padesSigner;
    private readonly IAsicSSigner _asicSSigner;
    private readonly IAsicESigner _asicESigner;
    private readonly CadesProfileEnhancer _cadesProfileEnhancer;
    private readonly XadesProfileEnhancer _xadesProfileEnhancer;
    private readonly PadesLongTermUpdater _padesLongTermUpdater;
    private readonly ILongTermValidationDataProvider _validationDataProvider;

    public SignatureOrchestrator(
        ISigningProviderResolver providerResolver,
        ICadesBaselineBSigner cadesSigner,
        IXadesBaselineBSigner xadesSigner,
        IPadesBaselineBSigner padesSigner,
        IAsicSSigner asicSSigner,
        IAsicESigner asicESigner,
        CadesProfileEnhancer cadesProfileEnhancer,
        XadesProfileEnhancer xadesProfileEnhancer,
        PadesLongTermUpdater padesLongTermUpdater,
        ILongTermValidationDataProvider validationDataProvider)
    {
        _providerResolver = providerResolver ?? throw new ArgumentNullException(nameof(providerResolver));
        _cadesSigner = cadesSigner ?? throw new ArgumentNullException(nameof(cadesSigner));
        _xadesSigner = xadesSigner ?? throw new ArgumentNullException(nameof(xadesSigner));
        _padesSigner = padesSigner ?? throw new ArgumentNullException(nameof(padesSigner));
        _asicSSigner = asicSSigner ?? throw new ArgumentNullException(nameof(asicSSigner));
        _asicESigner = asicESigner ?? throw new ArgumentNullException(nameof(asicESigner));
        _cadesProfileEnhancer = cadesProfileEnhancer ?? throw new ArgumentNullException(nameof(cadesProfileEnhancer));
        _xadesProfileEnhancer = xadesProfileEnhancer ?? throw new ArgumentNullException(nameof(xadesProfileEnhancer));
        _padesLongTermUpdater = padesLongTermUpdater ?? throw new ArgumentNullException(nameof(padesLongTermUpdater));
        _validationDataProvider = validationDataProvider ?? throw new ArgumentNullException(nameof(validationDataProvider));
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

        var provider = _providerResolver.Resolve(providerType);
        var selector = certificateSelector
            ?? await ResolveDefaultCertificateSelectorAsync(provider, cancellationToken).ConfigureAwait(false);

        await using var buffer = new MemoryStream();
        await inputStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var input = buffer.ToArray();

        return format switch
        {
            SignatureFormat.CAdES => await SignCadesAsync(input, profile, provider, selector, cancellationToken)
                .ConfigureAwait(false),
            SignatureFormat.XAdES => await SignXadesAsync(input, profile, provider, selector, cancellationToken)
                .ConfigureAwait(false),
            SignatureFormat.PAdES => await SignPadesAsync(input, profile, provider, selector, appearance, cancellationToken)
                .ConfigureAwait(false),
            SignatureFormat.ASiC_S => await SignAsicSAsync(input, profile, provider, selector, cancellationToken)
                .ConfigureAwait(false),
            SignatureFormat.ASiC_E => await SignAsicEAsync(input, profile, provider, selector, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new UnsupportedSignatureFormatException(
                $"Signature format '{format}' is not supported.",
                format)
        };
    }

    private async Task<SignatureCreationResult> SignCadesAsync(
        byte[] input,
        SignatureProfile profile,
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

        var cms = await EnhanceCadesAsync(result.CmsBytes, profile, provider, selector, cancellationToken)
            .ConfigureAwait(false);

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(cms, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.CAdES,
            Profile: profile,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private async Task<SignatureCreationResult> SignXadesAsync(
        byte[] input,
        SignatureProfile profile,
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

        var xml = result.SignedXmlUtf8;
        if (profile != SignatureProfile.B)
        {
            var signingCert = await GetSigningCertificateDerAsync(provider, selector, cancellationToken)
                .ConfigureAwait(false);
            xml = await _xadesProfileEnhancer
                .ApplyAsync(xml, profile, signingCert, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(xml, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.XAdES,
            Profile: profile,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private async Task<SignatureCreationResult> SignPadesAsync(
        byte[] input,
        SignatureProfile profile,
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

        var contentsHexLength = profile == SignatureProfile.B
            ? (int?)null
            : PadesBaselineBSigner.AdvancedProfileContentsHexLength;

        Func<byte[], CancellationToken, Task<byte[]>>? enhanceCms = profile == SignatureProfile.B
            ? null
            : (cms, ct) => EnhanceCadesAsync(cms, profile == SignatureProfile.LTA ? SignatureProfile.LT : profile, provider, selector, ct);

        var result = await _padesSigner.SignAsync(
            input,
            provider,
            selector,
            appearance: padesAppearance,
            cancellationToken: cancellationToken,
            contentsHexLength: contentsHexLength,
            enhanceCmsAsync: enhanceCms).ConfigureAwait(false);

        var pdf = result.SignedPdf;
        if (profile is SignatureProfile.LT or SignatureProfile.LTA)
        {
            var signingCert = await GetSigningCertificateDerAsync(provider, selector, cancellationToken)
                .ConfigureAwait(false);
            var material = await _validationDataProvider
                .CollectAsync(signingCert, cancellationToken)
                .ConfigureAwait(false);
            if (!material.HasRevocationEvidence)
            {
                throw new LongTermValidationDataUnavailableException(
                    "Baseline LT/LTA requires CRL or OCSP evidence. OpenSignature does not fabricate revocation data.");
            }

            pdf = _padesLongTermUpdater.AppendDss(pdf, material);
        }

        if (profile == SignatureProfile.LTA)
        {
            pdf = await _padesLongTermUpdater
                .AppendDocumentTimestampAsync(pdf, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(pdf, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.PAdES,
            Profile: profile,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private async Task<SignatureCreationResult> SignAsicSAsync(
        byte[] input,
        SignatureProfile profile,
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        var result = await _asicSSigner.SignAsync(
            input,
            provider,
            selector,
            cancellationToken: cancellationToken,
            enhanceCmsAsync: CreateCadesEnhancer(profile, provider, selector)).ConfigureAwait(false);

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(result.ContainerBytes, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.ASiC_S,
            Profile: profile,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private async Task<SignatureCreationResult> SignAsicEAsync(
        byte[] input,
        SignatureProfile profile,
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        var result = await _asicESigner.SignAsync(
            input,
            provider,
            selector,
            cancellationToken: cancellationToken,
            enhanceCmsAsync: CreateCadesEnhancer(profile, provider, selector)).ConfigureAwait(false);

        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false);
        return new SignatureCreationResult(
            Content: new MemoryStream(result.ContainerBytes, writable: false),
            ContentType: result.ContentType,
            Format: SignatureFormat.ASiC_E,
            Profile: profile,
            ProviderId: provider.ProviderId,
            CertificateThumbprint: cert!.Thumbprint.Value);
    }

    private Func<byte[], CancellationToken, Task<byte[]>>? CreateCadesEnhancer(
        SignatureProfile profile,
        ISigningProvider provider,
        SigningCertificateSelector selector)
    {
        if (profile == SignatureProfile.B)
        {
            return null;
        }

        return (cms, ct) => EnhanceCadesAsync(cms, profile, provider, selector, ct);
    }

    private async Task<byte[]> EnhanceCadesAsync(
        byte[] cmsBytes,
        SignatureProfile profile,
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        if (profile == SignatureProfile.B)
        {
            return cmsBytes;
        }

        var signingCert = await GetSigningCertificateDerAsync(provider, selector, cancellationToken)
            .ConfigureAwait(false);
        return await _cadesProfileEnhancer
            .ApplyAsync(cmsBytes, profile, signingCert, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]> GetSigningCertificateDerAsync(
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        var cert = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Signing certificate was not found for the provided selector.");
        if (cert.PublicCertificateDer.Count == 0)
        {
            throw new InvalidOperationException("Signing certificate does not expose public DER material.");
        }

        return cert.PublicCertificateDer is byte[] der ? der : cert.PublicCertificateDer.ToArray();
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
