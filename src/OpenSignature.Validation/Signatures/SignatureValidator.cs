using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Validation.Certificates;

namespace OpenSignature.Validation.Signatures;

/// <summary>
/// Signature validation for MVP Baseline B formats using BCL SignedCms / SignedXml
/// and OpenSignature PAdES ByteRange helpers.
/// </summary>
/// <remarks>
/// Limitations: not a full ETSI EN 319 102-1 validation report (no AdES attribute policy engine,
/// no LTV evidence, no ASiC containers). Cryptographic verification + certificate pipeline only.
/// </remarks>
public sealed class SignatureValidator : ISignatureValidator
{
    private readonly ICertificateValidator _certificateValidator;

    public SignatureValidator(ICertificateValidator? certificateValidator = null)
    {
        _certificateValidator = certificateValidator ?? new CertificateValidator();
    }

    public async Task<SignatureValidationResult> ValidateAsync(
        SignatureValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var checkedAt = DateTimeOffset.UtcNow;

        return request.Format switch
        {
            SignatureFormat.CAdES => await ValidateCadesAsync(request, checkedAt, cancellationToken)
                .ConfigureAwait(false),
            SignatureFormat.XAdES => await ValidateXadesAsync(request, checkedAt, cancellationToken)
                .ConfigureAwait(false),
            SignatureFormat.PAdES => await ValidatePadesAsync(request, checkedAt, cancellationToken)
                .ConfigureAwait(false),
            _ => new SignatureValidationResult(
                isValid: false,
                request.Format,
                [SignatureValidationCodes.SigFormatUnsupported],
                detail: $"Format {request.Format} is not supported by the MVP signature validator (Baseline B CAdES/XAdES/PAdES only).",
                checkedAt: checkedAt)
        };
    }

    private async Task<SignatureValidationResult> ValidateCadesAsync(
        SignatureValidationRequest request,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var detached = request.OriginalContent is not null;
            if (detached)
            {
                CmsSignatureHelper.ValidateSignedCms(request.SignedBytes, request.OriginalContent, verifySignatureOnly: true);
            }
            else
            {
                CmsSignatureHelper.ValidateSignedCms(request.SignedBytes, detachedContent: null, verifySignatureOnly: true);
            }

            using var signerCert = ExtractCmsSignerCertificate(request.SignedBytes, request.OriginalContent);
            if (signerCert is null)
            {
                return new SignatureValidationResult(
                    isValid: false,
                    SignatureFormat.CAdES,
                    [SignatureValidationCodes.SigNoSigner],
                    cryptoValid: true,
                    checkedAt: checkedAt,
                    detail: "CMS signature verified but no signer certificate was present.");
            }

            return await FinalizeAsync(
                SignatureFormat.CAdES,
                request,
                signerCert,
                cryptoValid: true,
                checkedAt,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException ex)
        {
            return CryptoFailure(SignatureFormat.CAdES, request, checkedAt, ex);
        }
        catch (InvalidOperationException ex)
        {
            return new SignatureValidationResult(
                isValid: false,
                SignatureFormat.CAdES,
                [SignatureValidationCodes.SigParseFailed],
                checkedAt: checkedAt,
                detail: ex.Message);
        }
    }

    private async Task<SignatureValidationResult> ValidateXadesAsync(
        SignatureValidationRequest request,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var ok = XadesBaselineBSigner.CheckSignature(request.SignedBytes);
            if (!ok)
            {
                return new SignatureValidationResult(
                    isValid: false,
                    SignatureFormat.XAdES,
                    [SignatureValidationCodes.SigCryptoInvalid, SignatureValidationCodes.SigDocumentModified],
                    cryptoValid: false,
                    checkedAt: checkedAt,
                    detail: "XMLDSig/XAdES signature check failed (tampered document or invalid signature).");
            }

            using var signerCert = ExtractXadesSignerCertificate(request.SignedBytes);
            if (signerCert is null)
            {
                return new SignatureValidationResult(
                    isValid: false,
                    SignatureFormat.XAdES,
                    [SignatureValidationCodes.SigNoSigner],
                    cryptoValid: true,
                    checkedAt: checkedAt,
                    detail: "XAdES signature verified but KeyInfo certificate was not found.");
            }

            return await FinalizeAsync(
                SignatureFormat.XAdES,
                request,
                signerCert,
                cryptoValid: true,
                checkedAt,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CryptographicException or XmlException or InvalidOperationException)
        {
            return new SignatureValidationResult(
                isValid: false,
                SignatureFormat.XAdES,
                [SignatureValidationCodes.SigParseFailed],
                checkedAt: checkedAt,
                detail: ex.Message);
        }
    }

    private async Task<SignatureValidationResult> ValidatePadesAsync(
        SignatureValidationRequest request,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            PadesBaselineBSigner.ValidateSignedPdf(request.SignedBytes, verifySignatureOnly: true);

            var cms = PdfByteRangeHelper.ExtractCmsFromContents(request.SignedBytes);
            var placeholder = PdfByteRangeHelper.FindContentsPlaceholder(
                request.SignedBytes,
                PadesBaselineBSigner.ContentsHexLength);
            var data = ExtractByteRangeBytes(request.SignedBytes, placeholder.ByteRange);
            using var signerCert = ExtractCmsSignerCertificate(cms, data);
            if (signerCert is null)
            {
                return new SignatureValidationResult(
                    isValid: false,
                    SignatureFormat.PAdES,
                    [SignatureValidationCodes.SigNoSigner],
                    cryptoValid: true,
                    checkedAt: checkedAt,
                    detail: "PAdES CMS verified but no signer certificate was present.");
            }

            return await FinalizeAsync(
                SignatureFormat.PAdES,
                request,
                signerCert,
                cryptoValid: true,
                checkedAt,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException ex)
        {
            return CryptoFailure(SignatureFormat.PAdES, request, checkedAt, ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new SignatureValidationResult(
                isValid: false,
                SignatureFormat.PAdES,
                [SignatureValidationCodes.SigParseFailed],
                checkedAt: checkedAt,
                detail: ex.Message);
        }
    }

    private async Task<SignatureValidationResult> FinalizeAsync(
        SignatureFormat format,
        SignatureValidationRequest request,
        X509Certificate2 signerCert,
        bool cryptoValid,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedSignerThumbprint is not null
            && !string.Equals(
                request.ExpectedSignerThumbprint,
                signerCert.Thumbprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureValidationResult(
                isValid: false,
                format,
                [SignatureValidationCodes.SigUnexpectedSigner],
                signerThumbprint: signerCert.Thumbprint,
                signerSubject: signerCert.Subject,
                cryptoValid: cryptoValid,
                checkedAt: checkedAt,
                detail: "Signer certificate thumbprint does not match the expected value.");
        }

        var certOptions = request.CertificateOptions ?? CreateDefaultCertOptions(signerCert);
        var certResult = await _certificateValidator
            .ValidateAsync(signerCert, certOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!certResult.IsValid)
        {
            var codes = new List<string> { SignatureValidationCodes.SigCertificateInvalid };
            codes.AddRange(certResult.ReasonCodes);
            return new SignatureValidationResult(
                isValid: false,
                format,
                codes,
                certificateResult: certResult,
                signerThumbprint: signerCert.Thumbprint,
                signerSubject: signerCert.Subject,
                cryptoValid: cryptoValid,
                checkedAt: checkedAt,
                detail: certResult.Detail);
        }

        return new SignatureValidationResult(
            isValid: true,
            format,
            [SignatureValidationCodes.SigValid],
            certificateResult: certResult,
            signerThumbprint: signerCert.Thumbprint,
            signerSubject: signerCert.Subject,
            cryptoValid: cryptoValid,
            checkedAt: checkedAt);
    }

    private static CertificateValidationOptions CreateDefaultCertOptions(X509Certificate2 signerCert)
    {
        var options = new CertificateValidationOptions
        {
            UseCustomTrustStore = true,
            RevocationMode = Revocation.RevocationMode.Offline,
            RequireSigningKeyUsage = true,
            AllowSelfSignedWhenTrusted = true
        };
        // Trust the embedded signer as root for MVP self-signed / demo certificates.
        options.TrustAnchors.Add(signerCert);
        return options;
    }

    private static SignatureValidationResult CryptoFailure(
        SignatureFormat format,
        SignatureValidationRequest request,
        DateTimeOffset checkedAt,
        CryptographicException ex)
    {
        var codes = new List<string> { SignatureValidationCodes.SigCryptoInvalid };
        if (request.OriginalContent is not null || format == SignatureFormat.PAdES || format == SignatureFormat.XAdES)
        {
            codes.Add(SignatureValidationCodes.SigDocumentModified);
        }

        return new SignatureValidationResult(
            isValid: false,
            format,
            codes,
            cryptoValid: false,
            checkedAt: checkedAt,
            detail: ex.Message);
    }

    private static X509Certificate2? ExtractCmsSignerCertificate(byte[] cmsBytes, byte[]? detachedContent)
    {
        SignedCms signedCms;
        if (detachedContent is not null)
        {
            signedCms = new SignedCms(new ContentInfo(detachedContent), detached: true);
            signedCms.Decode(cmsBytes);
        }
        else
        {
            signedCms = new SignedCms();
            signedCms.Decode(cmsBytes);
        }

        if (signedCms.SignerInfos.Count == 0)
        {
            return null;
        }

        var cert = signedCms.SignerInfos[0].Certificate;
        if (cert is not null)
        {
            return CertificateHelper.LoadPublic(cert.RawData);
        }

        if (signedCms.Certificates.Count > 0)
        {
            return CertificateHelper.LoadPublic(signedCms.Certificates[0].RawData);
        }

        return null;
    }

    private static X509Certificate2? ExtractXadesSignerCertificate(byte[] signedXmlUtf8)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(new MemoryStream(signedXmlUtf8));

        var signatureNode = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>()
            .FirstOrDefault();
        if (signatureNode is null)
        {
            return null;
        }

        var certNodes = signatureNode.GetElementsByTagName("X509Certificate", SignedXml.XmlDsigNamespaceUrl);
        if (certNodes.Count == 0)
        {
            return null;
        }

        var b64 = certNodes[0]!.InnerText.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        var der = Convert.FromBase64String(b64);
        return CertificateHelper.LoadPublic(der);
    }

    private static byte[] ExtractByteRangeBytes(byte[] pdf, IReadOnlyList<int> byteRange)
    {
        using var ms = new MemoryStream();
        for (var i = 0; i < byteRange.Count; i += 2)
        {
            ms.Write(pdf, byteRange[i], byteRange[i + 1]);
        }

        return ms.ToArray();
    }
}
