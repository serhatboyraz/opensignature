using OpenSignature.Domain.Enums;

namespace OpenSignature.Application.Verification;

/// <summary>Command for ad-hoc verification of an uploaded signed document.</summary>
public sealed class VerifyUploadedSignatureCommand
{
    public required SignatureFormat Format { get; init; }

    public required Stream SignedContent { get; init; }

    /// <summary>Original document bytes for detached CAdES (or detached XAdES). Ignored when null.</summary>
    public Stream? OriginalContent { get; init; }
}

/// <summary>Outcome of a verification use case.</summary>
public abstract record SignatureVerificationOutcome
{
    private SignatureVerificationOutcome()
    {
    }

    public sealed record Success(SignatureVerificationReportDto Report) : SignatureVerificationOutcome;

    public sealed record NotFound : SignatureVerificationOutcome
    {
        public static NotFound Instance { get; } = new();
    }

    public sealed record NotReady(string ErrorCode, string Detail) : SignatureVerificationOutcome;

    public sealed record InvalidRequest(string ErrorCode, string Detail) : SignatureVerificationOutcome;

    public sealed record TooLarge(string ErrorCode, string Detail) : SignatureVerificationOutcome;
}

/// <summary>JSON-serializable detailed verification report for API and UI.</summary>
public sealed record SignatureVerificationReportDto(
    string OverallStatus,
    bool IsValid,
    IReadOnlyList<string> ReasonCodes,
    DateTimeOffset CheckedAt,
    string Source,
    string? Format,
    Guid? SignatureId,
    string? Detail,
    string Limitations,
    SignatureCryptoCheckDto? Signature,
    CertificatePathReportDto? Certificate);

/// <summary>Cryptographic signature section of a verification report.</summary>
public sealed record SignatureCryptoCheckDto(
    bool CryptoValid,
    string? SignerThumbprint,
    string? SignerSubject,
    IReadOnlyList<string> ReasonCodes);

/// <summary>Certificate path section of a verification report.</summary>
public sealed record CertificatePathReportDto(
    bool IsValid,
    string? Subject,
    string? Issuer,
    string? Thumbprint,
    DateTimeOffset? NotBefore,
    DateTimeOffset? NotAfter,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> ChainStatus,
    RevocationReportDto? Revocation);

/// <summary>Revocation subsection of a verification report.</summary>
public sealed record RevocationReportDto(
    string Status,
    string Source,
    string? Detail);

/// <summary>Verification report source identifiers.</summary>
public static class VerificationReportSource
{
    public const string StoredSignature = "StoredSignature";
    public const string UploadedDocument = "UploadedDocument";
}

/// <summary>Human-readable limitation text for MVP Baseline B verification.</summary>
public static class VerificationReportLimitations
{
    public const string BaselineB =
        "Cryptographic verification and certificate path checks for CAdES, XAdES, PAdES, and ASiC " +
        "(ASiC unpacks the inner CAdES). Not a full ETSI EN 319 102-1 AdES conformance report: " +
        "T/LT/LTA timestamps and revocation evidence are not independently evaluated.";
}
