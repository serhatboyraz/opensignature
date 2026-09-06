using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Abstractions.Signatures;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Application.Messages;
using OpenSignature.Application.Signatures;
using OpenSignature.Domain;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;

namespace OpenSignature.Infrastructure.Signatures;

/// <summary>
/// EF Core + storage implementation of signature request use cases.
/// Persists metadata and outbox rows in one transaction; never waits for signing.
/// </summary>
public sealed class EfSignatureRequestService : ISignatureRequestService
{
    private readonly OpenSignatureDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IOptions<SignatureApiOptions> _options;
    private readonly ILogger<EfSignatureRequestService> _logger;

    public EfSignatureRequestService(
        OpenSignatureDbContext db,
        IFileStorage fileStorage,
        IOutboxWriter outboxWriter,
        IOptions<SignatureApiOptions> options,
        ILogger<EfSignatureRequestService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        _outboxWriter = outboxWriter ?? throw new ArgumentNullException(nameof(outboxWriter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CreateSignatureResult> CreateAsync(
        CreateSignatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Content);

        ValidateCreateCommand(command);

        var tenantId = command.TenantId;
        var idempotencyKey = NormalizeOptional(command.IdempotencyKey);

        if (idempotencyKey is not null)
        {
            var existing = await FindByIdempotencyKeyAsync(tenantId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return ToCreateResult(existing, wasCreated: false);
            }
        }

        var signatureId = Guid.CreateVersion7();
        var inputFileId = Guid.CreateVersion7();
        var createdAt = DateTimeOffset.UtcNow;
        var correlationId = command.CorrelationId ?? CorrelationId.New();
        var storageKey = SignatureStorageKeys.ForInput(tenantId, signatureId, createdAt);

        var contentType = string.IsNullOrWhiteSpace(command.ContentType)
            ? "application/octet-stream"
            : command.ContentType.Trim();
        var fileName = string.IsNullOrWhiteSpace(command.FileName)
            ? "upload.bin"
            : Path.GetFileName(command.FileName.Trim());

        FileStorageMetadata metadata;
        try
        {
            metadata = await _fileStorage
                .SaveAsync(storageKey, command.Content, contentType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to store input for signature {SignatureId} (tenant {TenantId})",
                signatureId,
                tenantId.Value);
            throw;
        }

        if (metadata.Size <= 0)
        {
            await TryDeleteStorageAsync(storageKey, cancellationToken).ConfigureAwait(false);
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                "Uploaded file must not be empty.");
        }

        if (metadata.Size > _options.Value.MaxUploadBytes)
        {
            await TryDeleteStorageAsync(storageKey, cancellationToken).ConfigureAwait(false);
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                $"Uploaded file exceeds the maximum size of {_options.Value.MaxUploadBytes} bytes.");
        }

        // Certificate thumbprint is forwarded on the job message for Worker certificate selection.
        var certificateThumbprint = NormalizeOptional(command.CertificateThumbprint);
        var appearance = await StoreAppearanceAsync(
                command,
                tenantId,
                signatureId,
                createdAt,
                cancellationToken)
            .ConfigureAwait(false);

        var storedFile = StoredFile.Create(
            storageKey: storageKey,
            originalFileName: fileName,
            contentType: contentType,
            size: metadata.Size,
            sha256: metadata.Sha256,
            createdAt: createdAt,
            id: inputFileId);

        var request = SignatureRequest.Create(
            tenantId: tenantId,
            correlationId: correlationId,
            format: command.Format,
            profile: command.Profile,
            inputFileId: inputFileId,
            signingProvider: command.SigningProvider,
            createdBy: command.CreatedBy,
            idempotencyKey: idempotencyKey,
            createdAt: createdAt,
            id: signatureId,
            appearance: appearance);

        request.MarkQueued(createdAt);

        var job = SigningJob.Create(request.Id, createdAt: createdAt);

        _db.StoredFiles.Add(storedFile);
        _db.SignatureRequests.Add(request);
        _db.SigningJobs.Add(job);

        _outboxWriter.EnqueueSigningJob(new SigningJobMessage(
            JobId: job.Id,
            TenantId: tenantId.Value,
            SignatureId: request.Id,
            InputPath: storageKey.Value,
            RequestedFormat: request.Format,
            RequestedProfile: request.Profile,
            CreatedAt: request.CreatedAt,
            Attempt: job.Attempt,
            CorrelationId: request.CorrelationId.Value,
            CertificateThumbprint: certificateThumbprint));

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && idempotencyKey is not null)
        {
            _db.ChangeTracker.Clear();

            await TryDeleteStorageAsync(storageKey, cancellationToken).ConfigureAwait(false);
            if (appearance.ImageFileId is not null)
            {
                await TryDeleteStorageAsync(
                        SignatureStorageKeys.ForAppearance(tenantId, signatureId, createdAt),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var raced = await FindByIdempotencyKeyAsync(tenantId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (raced is null)
            {
                throw new InvalidOperationException(
                    "Unique constraint conflict on signature request idempotency key, but no existing row was found.",
                    ex);
            }

            return ToCreateResult(raced, wasCreated: false);
        }

        _logger.LogInformation(
            "Queued signature request {SignatureId} job {JobId} for tenant {TenantId} (correlation {CorrelationId})",
            request.Id,
            job.Id,
            tenantId.Value,
            request.CorrelationId.Value);

        return ToCreateResult(request, wasCreated: true);
    }

    /// <inheritdoc />
    public async Task<SignatureRequestStatusDto?> GetAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        EnsureSignatureId(signatureId);

        var request = await _db.SignatureRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == signatureId && r.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return request is null ? null : ToStatusDto(request);
    }

    /// <inheritdoc />
    public async Task<SignatureContentResult> GetContentAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        EnsureSignatureId(signatureId);

        var request = await _db.SignatureRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == signatureId && r.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return SignatureContentResult.NotFound.Instance;
        }

        if (request.Status != SignatureStatus.Completed || request.OutputFileId is null)
        {
            return new SignatureContentResult.NotReady(
                request.Status,
                "SIGNATURE_OUTPUT_NOT_FOUND",
                "Signed content is available only when the signature request status is Completed.");
        }

        var outputFile = await _db.StoredFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == request.OutputFileId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (outputFile is null || outputFile.IsDeleted)
        {
            return new SignatureContentResult.NotReady(
                request.Status,
                "SIGNATURE_OUTPUT_NOT_FOUND",
                "Signed content metadata was not found.");
        }

        var stream = await _fileStorage
            .OpenReadAsync(outputFile.StorageKey, cancellationToken)
            .ConfigureAwait(false);

        return new SignatureContentResult.Success(
            stream,
            outputFile.ContentType,
            outputFile.OriginalFileName,
            outputFile.Size);
    }

    /// <inheritdoc />
    public async Task<CancelSignatureResult> CancelAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        EnsureSignatureId(signatureId);

        var request = await _db.SignatureRequests
            .SingleOrDefaultAsync(
                r => r.Id == signatureId && r.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return CancelSignatureResult.NotFound.Instance;
        }

        if (!SignatureStatusTransitions.CanTransition(request.Status, SignatureStatus.Cancelled))
        {
            return new CancelSignatureResult.Conflict(
                "SIGNATURE_NOT_CANCELLABLE",
                $"Signature request in status {request.Status} cannot be cancelled.");
        }

        try
        {
            request.MarkCancelled();
        }
        catch (DomainException ex)
        {
            return new CancelSignatureResult.Conflict("SIGNATURE_NOT_CANCELLABLE", ex.Message);
        }

        var job = await _db.SigningJobs
            .SingleOrDefaultAsync(j => j.SignatureRequestId == signatureId, cancellationToken)
            .ConfigureAwait(false);

        if (job is not null &&
            job.Status is not (SigningJobStatus.Completed or SigningJobStatus.Cancelled))
        {
            job.MarkCancelled();
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Cancelled signature request {SignatureId} for tenant {TenantId}",
            signatureId,
            tenantId.Value);

        return new CancelSignatureResult.Success(ToStatusDto(request));
    }

    private void ValidateCreateCommand(CreateSignatureCommand command)
    {
        if (!Enum.IsDefined(command.Format))
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_FORMAT_UNSUPPORTED",
                $"Signature format '{command.Format}' is not supported.");
        }

        if (!Enum.IsDefined(command.Profile))
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_PROFILE_UNSUPPORTED",
                $"Signature profile '{command.Profile}' is not supported.");
        }

        if (!Enum.IsDefined(command.SigningProvider))
        {
            throw new SignatureRequestValidationException(
                "SIGNING_PROVIDER_UNAVAILABLE",
                $"Signing provider '{command.SigningProvider}' is not supported.");
        }

        var thumbprint = NormalizeOptional(command.CertificateThumbprint);
        if (thumbprint is not null)
        {
            try
            {
                _ = CertificateThumbprint.Create(thumbprint);
            }
            catch (ArgumentException ex)
            {
                throw new SignatureRequestValidationException(
                    "SIGNING_CERTIFICATE_NOT_FOUND",
                    ex.Message);
            }
        }

        ValidateAppearance(command);
    }

    private static void ValidateAppearance(CreateSignatureCommand command)
    {
        var hasNote = !string.IsNullOrWhiteSpace(command.SignatureNote);
        var hasImage = command.AppearanceImage is not null;
        var visible = command.VisibleSignature || hasNote || hasImage;
        if (!visible)
        {
            return;
        }

        if (command.Format != SignatureFormat.PAdES)
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                "Visible signature appearance (note or image) is only supported for PAdES.");
        }

        if (command.AppearancePageNumber < 1)
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                "Appearance page number must be at least 1.");
        }

        if (command.SignatureNote is { Length: > PadesAppearanceSettings.MaxNoteLength })
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                $"Signature note must not exceed {PadesAppearanceSettings.MaxNoteLength} characters.");
        }
    }

    private async Task<PadesAppearanceSettings> StoreAppearanceAsync(
        CreateSignatureCommand command,
        TenantId tenantId,
        Guid signatureId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var hasNote = !string.IsNullOrWhiteSpace(command.SignatureNote);
        var hasImage = command.AppearanceImage is not null;
        var visible = command.VisibleSignature || hasNote || hasImage;
        if (!visible)
        {
            return PadesAppearanceSettings.Create(visible: false);
        }

        Guid? imageFileId = null;
        if (hasImage)
        {
            imageFileId = await SaveAppearanceImageAsync(
                    command,
                    tenantId,
                    signatureId,
                    createdAt,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            return PadesAppearanceSettings.Create(
                visible: true,
                note: command.SignatureNote,
                imageFileId: imageFileId,
                pageNumber: command.AppearancePageNumber < 1 ? 1 : command.AppearancePageNumber);
        }
        catch (ArgumentException ex)
        {
            throw new SignatureRequestValidationException("SIGNATURE_REQUEST_INVALID", ex.Message);
        }
    }

    private async Task<Guid> SaveAppearanceImageAsync(
        CreateSignatureCommand command,
        TenantId tenantId,
        Guid signatureId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var stream = command.AppearanceImage
            ?? throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                "Appearance image stream is missing.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                "Appearance image must not be empty.");
        }

        if (bytes.Length > _options.Value.MaxAppearanceImageBytes)
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                $"Appearance image exceeds the maximum size of {_options.Value.MaxAppearanceImageBytes} bytes.");
        }

        if (!IsAllowedAppearanceImage(bytes))
        {
            throw new SignatureRequestValidationException(
                "SIGNATURE_REQUEST_INVALID",
                "Appearance image must be JPEG or PNG.");
        }

        var imageId = Guid.CreateVersion7();
        var storageKey = SignatureStorageKeys.ForAppearance(tenantId, signatureId, createdAt);
        var contentType = NormalizeAppearanceContentType(bytes, command.AppearanceImageContentType);
        var fileName = string.IsNullOrWhiteSpace(command.AppearanceImageFileName)
            ? "appearance.bin"
            : Path.GetFileName(command.AppearanceImageFileName.Trim());

        FileStorageMetadata metadata;
        try
        {
            await using var imageStream = new MemoryStream(bytes, writable: false);
            metadata = await _fileStorage
                .SaveAsync(storageKey, imageStream, contentType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to store appearance image for signature {SignatureId}",
                signatureId);
            throw;
        }

        var stored = StoredFile.Create(
            storageKey: storageKey,
            originalFileName: fileName,
            contentType: contentType,
            size: metadata.Size,
            sha256: metadata.Sha256,
            createdAt: createdAt,
            id: imageId);

        _db.StoredFiles.Add(stored);
        return imageId;
    }

    private static bool IsAllowedAppearanceImage(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return true;
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(png))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeAppearanceContentType(byte[] bytes, string? contentType)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return "image/jpeg";
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(png))
        {
            return "image/png";
        }

        return string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
    }

    private async Task<SignatureRequest?> FindByIdempotencyKeyAsync(
        TenantId tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _db.SignatureRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task TryDeleteStorageAsync(StorageKey storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await _fileStorage.DeleteAsync(storageKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete orphaned storage object {StorageKey}",
                storageKey.Value);
        }
    }

    private static CreateSignatureResult ToCreateResult(SignatureRequest request, bool wasCreated)
        => new(
            request.Id,
            request.Status,
            request.CreatedAt,
            StatusUrl(request.Id),
            wasCreated);

    private static SignatureRequestStatusDto ToStatusDto(SignatureRequest request)
        => new(
            request.Id,
            request.TenantId.Value,
            request.Status,
            request.Format,
            request.Profile,
            request.SigningProvider,
            request.CreatedAt,
            request.QueuedAt,
            request.StartedAt,
            request.CompletedAt,
            request.FailedAt,
            request.ErrorCode?.Value,
            request.ErrorMessage,
            request.CorrelationId.Value,
            request.Appearance.Visible,
            request.Appearance.Note,
            request.Appearance.PageNumber,
            request.Appearance.ImageFileId is not null);

    private static string StatusUrl(Guid id) => $"/api/v1/signatures/{id:D}";

    private static void EnsureSignatureId(Guid signatureId)
    {
        if (signatureId == Guid.Empty)
        {
            throw new ArgumentException("Signature ID must not be empty.", nameof(signatureId));
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
