using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Thrown when a requested signing provider is not registered or cannot be uniquely resolved.
/// </summary>
public sealed class UnsupportedSigningProviderException : Exception
{
    public static readonly ErrorCode DefaultErrorCode = ErrorCode.Create("SIGNING_PROVIDER_UNSUPPORTED");

    public UnsupportedSigningProviderException(
        string message,
        SigningProviderType? providerType = null,
        string? providerId = null,
        ErrorCode? errorCode = null)
        : base(message)
    {
        ProviderType = providerType;
        ProviderId = providerId;
        ErrorCode = errorCode ?? DefaultErrorCode;
    }

    public ErrorCode ErrorCode { get; }

    public SigningProviderType? ProviderType { get; }

    public string? ProviderId { get; }
}
