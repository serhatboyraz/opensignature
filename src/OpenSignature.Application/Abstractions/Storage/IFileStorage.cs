using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Abstractions.Storage;

/// <summary>
/// Port for streaming document storage. Callers must use server-generated storage keys.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Streams <paramref name="content"/> to storage under <paramref name="storageKey"/>,
    /// computing SHA-256 while writing.
    /// </summary>
    Task<FileStorageMetadata> SaveAsync(
        StorageKey storageKey,
        Stream content,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a readable stream for the object identified by <paramref name="storageKey"/>.
    /// </summary>
    Task<Stream> OpenReadAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether an object exists for <paramref name="storageKey"/>.
    /// </summary>
    Task<bool> ExistsAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the object identified by <paramref name="storageKey"/> if it exists.
    /// </summary>
    Task DeleteAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns storage metadata for <paramref name="storageKey"/>, or <c>null</c> when missing.
    /// </summary>
    Task<FileStorageMetadata?> GetMetadataAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default);
}
