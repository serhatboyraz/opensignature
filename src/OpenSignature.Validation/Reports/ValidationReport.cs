using OpenSignature.Domain.Enums;
using OpenSignature.Validation.Revocation;

namespace OpenSignature.Validation.Reports;

/// <summary>
/// JSON-serializable validation report combining certificate path and signature crypto checks.
/// </summary>
public sealed class ValidationReport
{
    public required string OverallStatus { get; init; }

    public required bool IsValid { get; init; }

    public required IReadOnlyList<string> ReasonCodes { get; init; }

    public SignatureFormat? Format { get; init; }

    public DateTimeOffset CheckedAt { get; init; }

    public SignatureCryptoCheck? Signature { get; init; }

    public CertificatePathReport? Certificate { get; init; }

    public string? Detail { get; init; }
}

/// <summary>Cryptographic signature check section of a validation report.</summary>
public sealed class SignatureCryptoCheck
{
    public required bool CryptoValid { get; init; }

    public string? SignerThumbprint { get; init; }

    public string? SignerSubject { get; init; }

    public required IReadOnlyList<string> ReasonCodes { get; init; }
}

/// <summary>Certificate path section of a validation report.</summary>
public sealed class CertificatePathReport
{
    public required bool IsValid { get; init; }

    public string? Subject { get; init; }

    public string? Issuer { get; init; }

    public string? Thumbprint { get; init; }

    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? NotAfter { get; init; }

    public required IReadOnlyList<string> ReasonCodes { get; init; }

    public required IReadOnlyList<string> ChainStatus { get; init; }

    public RevocationReport? Revocation { get; init; }
}

/// <summary>Revocation subsection of a certificate path report.</summary>
public sealed class RevocationReport
{
    public required string Status { get; init; }

    public required string Source { get; init; }

    public string? Detail { get; init; }

    public static RevocationReport? From(RevocationCheckResult? result)
    {
        if (result is null)
        {
            return null;
        }

        return new RevocationReport
        {
            Status = result.Status.ToString(),
            Source = result.Source,
            Detail = result.Detail
        };
    }
}

/// <summary>Overall status values for <see cref="ValidationReport"/>.</summary>
public static class ValidationReportStatus
{
    public const string Valid = "VALID";
    public const string Invalid = "INVALID";
    public const string Indeterminate = "INDETERMINATE";
}
