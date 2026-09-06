using OpenSignature.Validation.Revocation;

namespace OpenSignature.Validation.Certificates;

/// <summary>
/// Structured certificate validation outcome with machine-readable reason codes.
/// </summary>
public sealed class CertificateValidationResult
{
    public CertificateValidationResult(
        bool isValid,
        IReadOnlyList<string> reasonCodes,
        string? subject = null,
        string? issuer = null,
        string? thumbprint = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        IReadOnlyList<string>? chainStatus = null,
        RevocationCheckResult? revocation = null,
        DateTimeOffset? checkedAt = null,
        string? detail = null)
    {
        IsValid = isValid;
        ReasonCodes = reasonCodes ?? Array.Empty<string>();
        Subject = subject;
        Issuer = issuer;
        Thumbprint = thumbprint;
        NotBefore = notBefore;
        NotAfter = notAfter;
        ChainStatus = chainStatus ?? Array.Empty<string>();
        Revocation = revocation;
        CheckedAt = checkedAt ?? DateTimeOffset.UtcNow;
        Detail = detail;
    }

    public bool IsValid { get; }

    public IReadOnlyList<string> ReasonCodes { get; }

    public string? Subject { get; }

    public string? Issuer { get; }

    public string? Thumbprint { get; }

    public DateTimeOffset? NotBefore { get; }

    public DateTimeOffset? NotAfter { get; }

    public IReadOnlyList<string> ChainStatus { get; }

    public RevocationCheckResult? Revocation { get; }

    public DateTimeOffset CheckedAt { get; }

    public string? Detail { get; }

    public static CertificateValidationResult Failure(
        string reasonCode,
        string? detail = null,
        string? subject = null,
        string? issuer = null,
        string? thumbprint = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        IReadOnlyList<string>? chainStatus = null,
        RevocationCheckResult? revocation = null,
        DateTimeOffset? checkedAt = null)
        => new(
            isValid: false,
            reasonCodes: [reasonCode],
            subject,
            issuer,
            thumbprint,
            notBefore,
            notAfter,
            chainStatus,
            revocation,
            checkedAt,
            detail);

    public static CertificateValidationResult Success(
        string? subject,
        string? issuer,
        string? thumbprint,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        IReadOnlyList<string>? chainStatus,
        RevocationCheckResult? revocation,
        DateTimeOffset? checkedAt = null)
        => new(
            isValid: true,
            reasonCodes: [CertificateValidationCodes.CertValid],
            subject,
            issuer,
            thumbprint,
            notBefore,
            notAfter,
            chainStatus,
            revocation,
            checkedAt);
}
