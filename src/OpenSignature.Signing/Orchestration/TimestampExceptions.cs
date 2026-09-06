using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Signing.Orchestration;

/// <summary>
/// Thrown when a requested T/LT/LTA profile cannot obtain an RFC 3161 timestamp.
/// Profiles are never silently downgraded to Baseline B.
/// </summary>
public sealed class TimestampAuthorityUnavailableException : Exception
{
    public static readonly ErrorCode DefaultErrorCode = ErrorCode.Create("TIMESTAMP_AUTHORITY_UNAVAILABLE");

    public TimestampAuthorityUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = DefaultErrorCode;
    }

    public ErrorCode ErrorCode { get; }
}

/// <summary>Thrown when RFC 3161 timestamping fails (invalid response, imprint mismatch, HTTP error).</summary>
public sealed class TimestampOperationFailedException : Exception
{
    public static readonly ErrorCode DefaultErrorCode = ErrorCode.Create("TIMESTAMP_OPERATION_FAILED");

    public TimestampOperationFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = DefaultErrorCode;
    }

    public ErrorCode ErrorCode { get; }
}

/// <summary>
/// Thrown when LT/LTA is requested but certificate/revocation evidence cannot be collected.
/// Profiles are never silently downgraded.
/// </summary>
public sealed class LongTermValidationDataUnavailableException : Exception
{
    public static readonly ErrorCode DefaultErrorCode = ErrorCode.Create("SIGNATURE_VALIDATION_DATA_UNAVAILABLE");

    public LongTermValidationDataUnavailableException(string message)
        : base(message)
    {
        ErrorCode = DefaultErrorCode;
    }

    public ErrorCode ErrorCode { get; }
}
