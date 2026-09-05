using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Abstractions.Storage;

/// <summary>
/// Metadata for an object stored via <see cref="IFileStorage"/>.
/// </summary>
public sealed record FileStorageMetadata(
    StorageKey StorageKey,
    long Size,
    Sha256Hash Sha256,
    string? ContentType);
