namespace OpenSignature.Application.Abstractions.Signing;

/// <summary>
/// Optional visible PAdES stamp passed into cryptographic signing.
/// Image bytes are loaded by the worker from storage — never from RabbitMQ.
/// </summary>
public sealed record SignatureAppearanceOptions(
    bool Visible,
    string? Note,
    int PageNumber,
    byte[]? ImageBytes,
    string? ImageContentType);
