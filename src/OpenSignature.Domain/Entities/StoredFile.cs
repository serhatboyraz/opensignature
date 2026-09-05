using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Entities;

public sealed class StoredFile
{
    private StoredFile(
        Guid id,
        StorageKey storageKey,
        string originalFileName,
        string contentType,
        long size,
        Sha256Hash sha256,
        DateTimeOffset createdAt,
        DateTimeOffset? deletedAt)
    {
        Id = id;
        StorageKey = storageKey;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        Size = size;
        Sha256 = sha256;
        CreatedAt = createdAt;
        DeletedAt = deletedAt;
    }

    public Guid Id { get; private set; }

    public StorageKey StorageKey { get; private set; }

    public string OriginalFileName { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    public Sha256Hash Sha256 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static StoredFile Create(
        StorageKey storageKey,
        string originalFileName,
        string contentType,
        long size,
        Sha256Hash sha256,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        ArgumentNullException.ThrowIfNull(sha256);

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("Original file name must not be empty.", nameof(originalFileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type must not be empty.", nameof(contentType));
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "File size must not be negative.");
        }

        return new StoredFile(
            id: Guid.CreateVersion7(),
            storageKey: storageKey,
            originalFileName: originalFileName.Trim(),
            contentType: contentType.Trim(),
            size: size,
            sha256: sha256,
            createdAt: createdAt ?? DateTimeOffset.UtcNow,
            deletedAt: null);
    }

    public void MarkDeleted(DateTimeOffset? deletedAt = null)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        DeletedAt = deletedAt ?? DateTimeOffset.UtcNow;
    }
}
