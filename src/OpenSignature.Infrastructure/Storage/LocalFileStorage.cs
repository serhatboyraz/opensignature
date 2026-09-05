using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Infrastructure.Storage;

/// <summary>
/// Local filesystem implementation of <see cref="IFileStorage"/> with atomic writes and SHA-256 hashing.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private const string MetadataExtension = ".osmeta";
    private const int BufferSize = 81920;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _rootPath;

    public LocalFileStorage(IOptions<LocalFileStorageOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    public LocalFileStorage(LocalFileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new ArgumentException("Root path must not be empty.", nameof(options));
        }

        _rootPath = Path.GetFullPath(options.RootPath.Trim());
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<FileStorageMetadata> SaveAsync(
        StorageKey storageKey,
        Stream content,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        ArgumentNullException.ThrowIfNull(content);

        var finalPath = ResolvePathUnderRoot(storageKey);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Unable to resolve storage directory.");

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Guid.CreateVersion7():N}.tmp");
        var metadataPath = GetMetadataPath(finalPath);
        var metadataTempPath = Path.Combine(directory, $".{Guid.CreateVersion7():N}.osmeta.tmp");

        try
        {
            long size;
            string sha256Hex;

            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                (size, sha256Hex) = await CopyAndHashAsync(content, fileStream, cancellationToken)
                    .ConfigureAwait(false);
                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
                ? null
                : contentType.Trim();

            var sidecar = new StoredObjectSidecar(sha256Hex, size, normalizedContentType);
            var sidecarJson = JsonSerializer.Serialize(sidecar, JsonOptions);
            await File.WriteAllTextAsync(metadataTempPath, sidecarJson, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            File.Move(tempPath, finalPath, overwrite: true);
            File.Move(metadataTempPath, metadataPath, overwrite: true);

            return new FileStorageMetadata(
                storageKey,
                size,
                Sha256Hash.Create(sha256Hex),
                normalizedContentType);
        }
        catch
        {
            TryDelete(tempPath);
            TryDelete(metadataTempPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePathUnderRoot(storageKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Storage object '{storageKey.Value}' was not found.", path);
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePathUnderRoot(storageKey);
        return Task.FromResult(File.Exists(path));
    }

    public Task DeleteAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePathUnderRoot(storageKey);
        TryDelete(path);
        TryDelete(GetMetadataPath(path));
        return Task.CompletedTask;
    }

    public async Task<FileStorageMetadata?> GetMetadataAsync(
        StorageKey storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePathUnderRoot(storageKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var metadataPath = GetMetadataPath(path);
        if (File.Exists(metadataPath))
        {
            await using var metaStream = new FileStream(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var sidecar = await JsonSerializer
                .DeserializeAsync<StoredObjectSidecar>(metaStream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (sidecar is not null && !string.IsNullOrWhiteSpace(sidecar.Sha256))
            {
                return new FileStorageMetadata(
                    storageKey,
                    sidecar.Size,
                    Sha256Hash.Create(sidecar.Sha256),
                    sidecar.ContentType);
            }
        }

        await using var contentStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var (size, sha256Hex) = await CopyAndHashAsync(contentStream, Stream.Null, cancellationToken)
            .ConfigureAwait(false);

        return new FileStorageMetadata(
            storageKey,
            size,
            Sha256Hash.Create(sha256Hex),
            ContentType: null);
    }

    private string ResolvePathUnderRoot(StorageKey storageKey)
    {
        var relative = storageKey.Value.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_rootPath, relative));
        var relativeToRoot = Path.GetRelativePath(_rootPath, combined);

        if (relativeToRoot.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativeToRoot))
        {
            throw new ArgumentException(
                $"Storage key '{storageKey.Value}' resolves outside the configured storage root.",
                nameof(storageKey));
        }

        return combined;
    }

    private static string GetMetadataPath(string objectPath) => objectPath + MetadataExtension;

    private static async Task<(long Size, string Sha256Hex)> CopyAndHashAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hasher.AppendData(buffer.AsSpan(0, read));
                if (!ReferenceEquals(destination, Stream.Null))
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                }

                size += read;
            }

            var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            return (size, hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup for temp/orphan files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for temp/orphan files.
        }
    }

    private sealed record StoredObjectSidecar(string Sha256, long Size, string? ContentType);
}
