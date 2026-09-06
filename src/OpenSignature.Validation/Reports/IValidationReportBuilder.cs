using OpenSignature.Validation.Certificates;
using OpenSignature.Validation.Signatures;

namespace OpenSignature.Validation.Reports;

/// <summary>Builds structured, JSON-serializable validation reports.</summary>
public interface IValidationReportBuilder
{
    /// <summary>Builds a report from a signature validation result.</summary>
    ValidationReport Build(SignatureValidationResult signatureResult);

    /// <summary>Builds a report from a certificate-only validation result.</summary>
    ValidationReport Build(CertificateValidationResult certificateResult);
}
