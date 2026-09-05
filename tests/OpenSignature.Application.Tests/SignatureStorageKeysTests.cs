using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Application.Tests;

public sealed class SignatureStorageKeysTests
{
    [Fact]
    public void ForInput_builds_expected_key()
    {
        var tenantId = TenantId.Create("tenant-a");
        var signatureId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var timestamp = new DateTimeOffset(2026, 9, 6, 15, 30, 0, TimeSpan.FromHours(3));

        var key = SignatureStorageKeys.ForInput(tenantId, signatureId, timestamp);

        Assert.Equal(
            "tenants/tenant-a/signatures/2026/09/06/11111111-2222-3333-4444-555555555555/input.bin",
            key.Value);
    }

    [Fact]
    public void ForSigned_builds_expected_key()
    {
        var tenantId = TenantId.Create("tenant-b");
        var signatureId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var timestamp = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var key = SignatureStorageKeys.ForSigned(tenantId, signatureId, timestamp);

        Assert.Equal(
            "tenants/tenant-b/signatures/2026/01/02/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/signed.bin",
            key.Value);
    }

    [Fact]
    public void ForInput_rejects_tenant_with_path_separator()
    {
        var tenantId = TenantId.Create("evil/tenant");

        Assert.Throws<ArgumentException>(() =>
            SignatureStorageKeys.ForInput(tenantId, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StorageKey_rejects_path_traversal()
    {
        Assert.Throws<ArgumentException>(() => StorageKey.Create("../etc/passwd"));
        Assert.Throws<ArgumentException>(() => StorageKey.Create("tenants/../secrets"));
        Assert.Throws<ArgumentException>(() => StorageKey.Create(@"tenants\t1\input.bin"));
        Assert.Throws<ArgumentException>(() => StorageKey.Create("/absolute/path.bin"));
    }
}
