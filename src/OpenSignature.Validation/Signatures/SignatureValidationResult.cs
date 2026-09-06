using OpenSignature.Domain.Enums;
using OpenSignature.Validation.Certificates;

namespace OpenSignature.Validation.Signatures;

/// <summary>
/// Structured signature validation outcome.
/// Cryptographic checks cover Baseline B formats only; not full ETSI EN 319 102-1 AdES validation.
/// </summary>
public sealed class SignatureValidationResult
{
    public SignatureValidationResult(
        bool isValid,
        SignatureFormat format,
        IReadOnlyList<string> reasonCodes,
        CertificateValidationResult? certificateResult = null,
        string? signerThumbprint = null,
        string? signerSubject = null,
        bool cryptoValid = false,
        DateTimeOffset? checkedAt = null,
        string? detail = null)
    {
        IsValid = isValid;
        Format = format;
        ReasonCodes = reasonCodes ?? Array.Empty<string>();
        CertificateResult = certificateResult;
        SignerThumbprint = signerThumbprint;
        SignerSubject = signerSubject;
        CryptoValid = cryptoValid;
        CheckedAt = checkedAt ?? DateTimeOffset.UtcNow;
        Detail = detail;
    }

    public bool IsValid { get; }

    public SignatureFormat Format { get; }

    public IReadOnlyList<string> ReasonCodes { get; }

    public CertificateValidationResult? CertificateResult { get; }

    public string? SignerThumbprint { get; }

    public string? SignerSubject { get; }

    public bool CryptoValid { get; }

    public DateTimeOffset CheckedAt { get; }

    public string? Detail { get; }
}
