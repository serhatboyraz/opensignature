using OpenSignature.Domain.Enums;

namespace OpenSignature.Application.Messages;

/// <summary>
/// RabbitMQ / outbox payload for a signing job. Carries metadata and storage references only — never document binaries.
/// </summary>
public sealed record SigningJobMessage(
    Guid JobId,
    string TenantId,
    Guid SignatureId,
    string InputPath,
    SignatureFormat RequestedFormat,
    SignatureProfile RequestedProfile,
    DateTimeOffset CreatedAt,
    int Attempt,
    string? CorrelationId = null,
    string? CertificateThumbprint = null);
