using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Application.Abstractions.Verification;
using OpenSignature.Application.Signatures;
using OpenSignature.Application.Verification;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Infrastructure.Storage;
using OpenSignature.Infrastructure.Verification;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Pfx;
using OpenSignature.Validation;
using Testcontainers.PostgreSql;

namespace OpenSignature.Infrastructure.Tests;

public sealed class SignatureVerificationServiceTests : IAsyncLifetime
{
    private const string PfxPassword = "verification-infra-test-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("opensignature")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    private string? _storageRoot;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _storageRoot = Path.Combine(
            Path.GetTempPath(),
            "opensignature-verify-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (_storageRoot is not null && Directory.Exists(_storageRoot))
        {
            try
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task VerifyStored_valid_attached_cades_is_VALID()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var verification = scope.ServiceProvider.GetRequiredService<ISignatureVerificationService>();

        var cms = await CreateAttachedCadesAsync(Encoding.UTF8.GetBytes("verify-me"));
        var (tenantId, signatureId) = await SeedCompletedSignatureAsync(
            db,
            storage,
            SignatureFormat.CAdES,
            cms,
            "application/pkcs7-mime");

        var outcome = await verification.VerifyStoredAsync(tenantId, signatureId);

        var success = Assert.IsType<SignatureVerificationOutcome.Success>(outcome);
        Assert.Equal("VALID", success.Report.OverallStatus);
        Assert.True(success.Report.IsValid);
        Assert.Equal(VerificationReportSource.StoredSignature, success.Report.Source);
        Assert.Equal(signatureId, success.Report.SignatureId);
        Assert.NotNull(success.Report.Signature);
        Assert.True(success.Report.Signature.CryptoValid);
        Assert.NotNull(success.Report.Certificate);
        Assert.Contains("SIG_VALID", success.Report.ReasonCodes);
    }

    [Fact]
    public async Task VerifyStored_modified_bytes_are_INVALID()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var verification = scope.ServiceProvider.GetRequiredService<ISignatureVerificationService>();

        var cms = await CreateAttachedCadesAsync(Encoding.UTF8.GetBytes("original-body"));
        cms[^1] ^= 0xFF;

        var (tenantId, signatureId) = await SeedCompletedSignatureAsync(
            db,
            storage,
            SignatureFormat.CAdES,
            cms,
            "application/pkcs7-mime");

        var outcome = await verification.VerifyStoredAsync(tenantId, signatureId);

        var success = Assert.IsType<SignatureVerificationOutcome.Success>(outcome);
        Assert.Equal("INVALID", success.Report.OverallStatus);
        Assert.False(success.Report.IsValid);
        Assert.Contains("SIG_CRYPTO_INVALID", success.Report.ReasonCodes);
    }

    [Fact]
    public async Task VerifyStored_queued_signature_is_not_ready()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var verification = scope.ServiceProvider.GetRequiredService<ISignatureVerificationService>();

        var tenantId = TenantId.Create("tenant-verify-queued");
        var signatureId = Guid.CreateVersion7();
        var createdAt = DateTimeOffset.UtcNow;
        var inputKey = SignatureStorageKeys.ForInput(tenantId, signatureId, createdAt);
        var payload = Encoding.UTF8.GetBytes("not-signed-yet");
        FileStorageMetadata metadata;
        await using (var stream = new MemoryStream(payload))
        {
            metadata = await storage.SaveAsync(inputKey, stream, "text/plain");
        }

        var inputFile = StoredFile.Create(
            inputKey,
            "doc.txt",
            "text/plain",
            metadata.Size,
            metadata.Sha256,
            createdAt);
        var request = SignatureRequest.Create(
            tenantId,
            CorrelationId.Create("corr-queued-verify"),
            SignatureFormat.CAdES,
            SignatureProfile.B,
            inputFile.Id,
            SigningProviderType.Pfx,
            "verification-tests",
            createdAt: createdAt,
            id: signatureId);
        request.MarkQueued(createdAt);

        db.StoredFiles.Add(inputFile);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();

        var outcome = await verification.VerifyStoredAsync(tenantId, signatureId);

        var notReady = Assert.IsType<SignatureVerificationOutcome.NotReady>(outcome);
        Assert.Equal("SIGNATURE_OUTPUT_NOT_FOUND", notReady.ErrorCode);
    }

    [Fact]
    public async Task VerifyUploaded_valid_attached_cades_is_VALID()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var verification = scope.ServiceProvider.GetRequiredService<ISignatureVerificationService>();

        var cms = await CreateAttachedCadesAsync(Encoding.UTF8.GetBytes("upload-verify"));
        await using var stream = new MemoryStream(cms);

        var outcome = await verification.VerifyUploadedAsync(new VerifyUploadedSignatureCommand
        {
            Format = SignatureFormat.CAdES,
            SignedContent = stream
        });

        var success = Assert.IsType<SignatureVerificationOutcome.Success>(outcome);
        Assert.Equal("VALID", success.Report.OverallStatus);
        Assert.True(success.Report.IsValid);
        Assert.Equal(VerificationReportSource.UploadedDocument, success.Report.Source);
        Assert.Null(success.Report.SignatureId);
    }

    [Fact]
    public async Task VerifyStored_unknown_id_is_not_found()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var verification = scope.ServiceProvider.GetRequiredService<ISignatureVerificationService>();

        var outcome = await verification.VerifyStoredAsync(
            TenantId.Create("tenant-verify-missing"),
            Guid.CreateVersion7());

        Assert.IsType<SignatureVerificationOutcome.NotFound>(outcome);
    }

    private async Task<ServiceProvider> BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddPersistence(_postgres.GetConnectionString());
        services.AddSingleton<IFileStorage>(new LocalFileStorage(new LocalFileStorageOptions
        {
            RootPath = _storageRoot!
        }));
        services.Configure<SignatureApiOptions>(_ => { });
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddOpenSignatureValidation();
        services.AddScoped<ISignatureVerificationService, SignatureVerificationService>();

        var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        await db.Database.MigrateAsync();
        return provider;
    }

    private static async Task<(TenantId TenantId, Guid SignatureId)> SeedCompletedSignatureAsync(
        OpenSignatureDbContext db,
        IFileStorage storage,
        SignatureFormat format,
        byte[] signedBytes,
        string contentType)
    {
        var tenantId = TenantId.Create("tenant-verify-1");
        var signatureId = Guid.CreateVersion7();
        var createdAt = DateTimeOffset.UtcNow;
        var inputKey = SignatureStorageKeys.ForInput(tenantId, signatureId, createdAt);
        var signedKey = SignatureStorageKeys.ForSigned(tenantId, signatureId, createdAt);

        FileStorageMetadata inputMeta;
        await using (var input = new MemoryStream(Encoding.UTF8.GetBytes("input")))
        {
            inputMeta = await storage.SaveAsync(inputKey, input, "text/plain");
        }

        FileStorageMetadata signedMeta;
        await using (var signed = new MemoryStream(signedBytes))
        {
            signedMeta = await storage.SaveAsync(signedKey, signed, contentType);
        }

        var inputFile = StoredFile.Create(
            inputKey,
            "input.txt",
            "text/plain",
            inputMeta.Size,
            inputMeta.Sha256,
            createdAt);
        var outputFile = StoredFile.Create(
            signedKey,
            "signed.bin",
            contentType,
            signedMeta.Size,
            signedMeta.Sha256,
            createdAt);

        var request = SignatureRequest.Create(
            tenantId,
            CorrelationId.Create("corr-verify-1"),
            format,
            SignatureProfile.B,
            inputFile.Id,
            SigningProviderType.Pfx,
            "verification-tests",
            createdAt: createdAt,
            id: signatureId);
        request.MarkQueued(createdAt);
        request.MarkProcessing(createdAt);
        request.MarkCompleted(outputFile.Id, createdAt);

        db.StoredFiles.Add(inputFile);
        db.StoredFiles.Add(outputFile);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();

        return (tenantId, signatureId);
    }

    private static async Task<byte[]> CreateAttachedCadesAsync(byte[] content)
    {
        using var material = EphemeralPfx.CreateRsa("CN=OpenSignature Verify Test", PfxPassword);
        await using var provider = new PfxSigningProvider(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-verify-test",
            Name = "Verify Test PFX",
            CertificateBytes = material.PfxBytes,
            Password = PfxPassword
        });
        var certs = await provider.ListCertificatesAsync();
        var selector = SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Attached, provider, selector);
        return signed.CmsBytes;
    }
}
