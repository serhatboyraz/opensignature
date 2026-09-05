using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Signatures;

/// <summary>
/// Command for <see cref="Abstractions.Signatures.ISignatureRequestService.CreateAsync"/>.
/// </summary>
public sealed class CreateSignatureCommand
{
    public required TenantId TenantId { get; init; }

    public required Stream Content { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required SignatureFormat Format { get; init; }

    public required SignatureProfile Profile { get; init; }

    public required SigningProviderType SigningProvider { get; init; }

    public string? CertificateThumbprint { get; init; }

    public string? IdempotencyKey { get; init; }

    public CorrelationId? CorrelationId { get; init; }

    public string CreatedBy { get; init; } = "api";
}
