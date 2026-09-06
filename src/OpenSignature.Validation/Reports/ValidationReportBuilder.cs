using OpenSignature.Validation.Certificates;
using OpenSignature.Validation.Signatures;

namespace OpenSignature.Validation.Reports;

/// <summary>
/// Default report builder. Produces DTOs suitable for System.Text.Json serialization.
/// </summary>
public sealed class ValidationReportBuilder : IValidationReportBuilder
{
    public ValidationReport Build(SignatureValidationResult signatureResult)
    {
        ArgumentNullException.ThrowIfNull(signatureResult);

        var codes = new List<string>(signatureResult.ReasonCodes);
        if (signatureResult.CertificateResult is not null)
        {
            foreach (var code in signatureResult.CertificateResult.ReasonCodes)
            {
                if (!codes.Contains(code, StringComparer.Ordinal))
                {
                    codes.Add(code);
                }
            }
        }

        var overall = signatureResult.IsValid
            ? ValidationReportStatus.Valid
            : IsIndeterminate(signatureResult)
                ? ValidationReportStatus.Indeterminate
                : ValidationReportStatus.Invalid;

        return new ValidationReport
        {
            OverallStatus = overall,
            IsValid = signatureResult.IsValid,
            ReasonCodes = codes,
            Format = signatureResult.Format,
            CheckedAt = signatureResult.CheckedAt,
            Detail = signatureResult.Detail,
            Signature = new SignatureCryptoCheck
            {
                CryptoValid = signatureResult.CryptoValid,
                SignerThumbprint = signatureResult.SignerThumbprint,
                SignerSubject = signatureResult.SignerSubject,
                ReasonCodes = signatureResult.ReasonCodes
            },
            Certificate = MapCertificate(signatureResult.CertificateResult)
        };
    }

    public ValidationReport Build(CertificateValidationResult certificateResult)
    {
        ArgumentNullException.ThrowIfNull(certificateResult);

        var overall = certificateResult.IsValid
            ? ValidationReportStatus.Valid
            : certificateResult.ReasonCodes.Contains(CertificateValidationCodes.CertRevocationUnknown)
                ? ValidationReportStatus.Indeterminate
                : ValidationReportStatus.Invalid;

        return new ValidationReport
        {
            OverallStatus = overall,
            IsValid = certificateResult.IsValid,
            ReasonCodes = certificateResult.ReasonCodes,
            Format = null,
            CheckedAt = certificateResult.CheckedAt,
            Detail = certificateResult.Detail,
            Signature = null,
            Certificate = MapCertificate(certificateResult)
        };
    }

    private static bool IsIndeterminate(SignatureValidationResult result) =>
        result.ReasonCodes.Contains(CertificateValidationCodes.CertRevocationUnknown)
        || (result.CertificateResult?.ReasonCodes.Contains(CertificateValidationCodes.CertRevocationUnknown) ?? false);

    private static CertificatePathReport? MapCertificate(CertificateValidationResult? result)
    {
        if (result is null)
        {
            return null;
        }

        return new CertificatePathReport
        {
            IsValid = result.IsValid,
            Subject = result.Subject,
            Issuer = result.Issuer,
            Thumbprint = result.Thumbprint,
            NotBefore = result.NotBefore,
            NotAfter = result.NotAfter,
            ReasonCodes = result.ReasonCodes,
            ChainStatus = result.ChainStatus,
            Revocation = RevocationReport.From(result.Revocation)
        };
    }
}
