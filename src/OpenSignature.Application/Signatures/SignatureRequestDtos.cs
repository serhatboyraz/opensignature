using OpenSignature.Domain.Enums;

namespace OpenSignature.Application.Signatures;

/// <summary>Accepted create response (HTTP 202).</summary>
public sealed record CreateSignatureResult(
    Guid Id,
    SignatureStatus Status,
    DateTimeOffset CreatedAt,
    string StatusUrl,
    bool WasCreated);

/// <summary>Public signature request status projection.</summary>
public sealed record SignatureRequestStatusDto(
    Guid Id,
    string TenantId,
    SignatureStatus Status,
    SignatureFormat Format,
    SignatureProfile Profile,
    SigningProviderType SigningProvider,
    DateTimeOffset CreatedAt,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? ErrorCode,
    string? ErrorMessage,
    string CorrelationId);

/// <summary>Outcome of opening signed content.</summary>
public abstract record SignatureContentResult
{
    private SignatureContentResult()
    {
    }

    public sealed record Success(Stream Content, string ContentType, string FileName, long Size)
        : SignatureContentResult;

    public sealed record NotFound : SignatureContentResult
    {
        public static NotFound Instance { get; } = new();
    }

    public sealed record NotReady(SignatureStatus Status, string ErrorCode, string Detail)
        : SignatureContentResult;
}

/// <summary>Outcome of cancel.</summary>
public abstract record CancelSignatureResult
{
    private CancelSignatureResult()
    {
    }

    public sealed record Success(SignatureRequestStatusDto Request) : CancelSignatureResult;

    public sealed record NotFound : CancelSignatureResult
    {
        public static NotFound Instance { get; } = new();
    }

    public sealed record Conflict(string ErrorCode, string Detail) : CancelSignatureResult;
}

/// <summary>Validation / domain failure for create.</summary>
public sealed class SignatureRequestValidationException : Exception
{
    public SignatureRequestValidationException(string errorCode, string detail)
        : base(detail)
    {
        ErrorCode = errorCode;
        Detail = detail;
    }

    public string ErrorCode { get; }

    public string Detail { get; }
}
