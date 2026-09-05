using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Signing.Orchestration;

/// <summary>
/// Thrown when a requested signature profile is not supported.
/// Profiles are never silently downgraded.
/// </summary>
public sealed class UnsupportedSignatureProfileException : Exception
{
    public static readonly ErrorCode DefaultErrorCode = ErrorCode.Create("SIGNATURE_PROFILE_UNSUPPORTED");

    public UnsupportedSignatureProfileException(
        string message,
        SignatureProfile requestedProfile,
        ErrorCode? errorCode = null)
        : base(message)
    {
        RequestedProfile = requestedProfile;
        ErrorCode = errorCode ?? DefaultErrorCode;
    }

    public SignatureProfile RequestedProfile { get; }

    public ErrorCode ErrorCode { get; }
}

/// <summary>
/// Thrown when a requested signature format is not supported by the signature engine.
/// </summary>
public sealed class UnsupportedSignatureFormatException : Exception
{
    public static readonly ErrorCode DefaultErrorCode = ErrorCode.Create("SIGNATURE_FORMAT_UNSUPPORTED");

    public UnsupportedSignatureFormatException(
        string message,
        SignatureFormat requestedFormat,
        ErrorCode? errorCode = null)
        : base(message)
    {
        RequestedFormat = requestedFormat;
        ErrorCode = errorCode ?? DefaultErrorCode;
    }

    public SignatureFormat RequestedFormat { get; }

    public ErrorCode ErrorCode { get; }
}
