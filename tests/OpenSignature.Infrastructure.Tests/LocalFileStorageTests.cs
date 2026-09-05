using System.Security.Cryptography;
using System.Text;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Storage;

namespace OpenSignature.Infrastructure.Tests;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "opensignature-storage-tests", Guid.CreateVersion7().ToString("N"));
        _storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = _root });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup for residual locked files on Windows.
        }
    }

    [Fact]
    public async Task Save_and_OpenRead_round_trip()
    {
        var key = SignatureStorageKeys.ForInput(
            TenantId.Create("tenant-1"),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        var payload = Encoding.UTF8.GetBytes("OpenSignature round-trip payload");
        await using (var input = new MemoryStream(payload))
        {
            var metadata = await _storage.SaveAsync(key, input, "application/octet-stream");
            Assert.Equal(payload.Length, metadata.Size);
            Assert.Equal("application/octet-stream", metadata.ContentType);
        }

        await using var read = await _storage.OpenReadAsync(key);
        using var memory = new MemoryStream();
        await read.CopyToAsync(memory);
        Assert.Equal(payload, memory.ToArray());
    }

    [Fact]
    public async Task Save_computes_correct_sha256()
    {
        var key = SignatureStorageKeys.ForSigned(
            TenantId.Create("tenant-hash"),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        var payload = Encoding.UTF8.GetBytes("hash-me-please");
        var expectedHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        await using var input = new MemoryStream(payload);
        var metadata = await _storage.SaveAsync(key, input);

        Assert.Equal(expectedHash, metadata.Sha256.Value);

        var stored = await _storage.GetMetadataAsync(key);
        Assert.NotNull(stored);
        Assert.Equal(expectedHash, stored.Sha256.Value);
        Assert.Equal(payload.Length, stored.Size);
    }

    [Fact]
    public async Task Concurrent_writes_to_different_keys_succeed()
    {
        var tenant = TenantId.Create("tenant-concurrent");
        var timestamp = DateTimeOffset.UtcNow;

        var tasks = Enumerable.Range(0, 8).Select(async i =>
        {
            var key = SignatureStorageKeys.ForInput(tenant, Guid.CreateVersion7(), timestamp);
            var bytes = Encoding.UTF8.GetBytes($"payload-{i}-{Guid.CreateVersion7():N}");
            await using var stream = new MemoryStream(bytes);
            var metadata = await _storage.SaveAsync(key, stream, "text/plain");

            Assert.True(await _storage.ExistsAsync(key));
            Assert.Equal(bytes.Length, metadata.Size);

            await using var read = await _storage.OpenReadAsync(key);
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer);
            Assert.Equal(bytes, buffer.ToArray());
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void Path_traversal_is_rejected_by_storage_key()
    {
        Assert.Throws<ArgumentException>(() => StorageKey.Create("../../outside.bin"));
        Assert.Throws<ArgumentException>(() => StorageKey.Create("tenants/../../outside.bin"));
    }

    [Fact]
    public void Drive_rooted_storage_key_is_rejected_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<ArgumentException>(() => StorageKey.Create("C:/Windows/Temp/opensignature-escape.bin"));
    }

    [Fact]
    public async Task Exists_Delete_and_missing_OpenRead_behave_as_expected()
    {
        var key = SignatureStorageKeys.ForInput(
            TenantId.Create("tenant-lifecycle"),
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow);

        Assert.False(await _storage.ExistsAsync(key));
        Assert.Null(await _storage.GetMetadataAsync(key));

        await using (var input = new MemoryStream([1, 2, 3, 4]))
        {
            await _storage.SaveAsync(key, input);
        }

        Assert.True(await _storage.ExistsAsync(key));
        await _storage.DeleteAsync(key);
        Assert.False(await _storage.ExistsAsync(key));

        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync(key));
    }

    [Fact]
    public void Constructor_rejects_empty_root()
    {
        Assert.Throws<ArgumentException>(() =>
            new LocalFileStorage(new LocalFileStorageOptions { RootPath = " " }));
    }
}
