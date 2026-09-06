using Microsoft.EntityFrameworkCore;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Abstractions.Persistence;
using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Application.Messages;
using OpenSignature.Application.Messaging;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Orchestration;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// Loads a queued signature request, invokes <see cref="ISignatureCreationService"/>,
/// persists signed output, and updates request/job status.
/// Processing is idempotent: an atomic job lock prevents duplicate signing under at-least-once delivery.
/// </summary>
public sealed class SignatureSigningJobProcessor : ISigningJobProcessor
{
    /// <summary>
    /// Lock lease duration. Expired Locked/Processing jobs may be reclaimed after worker crash/restart.
    /// Must exceed expected signing time to avoid mid-flight reclaim races.
    /// </summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private readonly OpenSignatureDbContext _db;
    private readonly ISigningJobLockService _jobLock;
    private readonly IFileStorage _fileStorage;
    private readonly ISignatureCreationService _signatureCreation;
    private readonly ILogger<SignatureSigningJobProcessor> _logger;

    public SignatureSigningJobProcessor(
        OpenSignatureDbContext db,
        ISigningJobLockService jobLock,
        IFileStorage fileStorage,
        ISignatureCreationService signatureCreation,
        ILogger<SignatureSigningJobProcessor> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _jobLock = jobLock ?? throw new ArgumentNullException(nameof(jobLock));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        _signatureCreation = signatureCreation ?? throw new ArgumentNullException(nameof(signatureCreation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessAsync(SigningJobMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = TenantId.Create(message.TenantId);

        var request = await _db.SignatureRequests
            .SingleOrDefaultAsync(
                r => r.Id == message.SignatureId && r.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            throw new PermanentSigningJobException(
                $"Signature request {message.SignatureId} was not found for tenant {message.TenantId}.");
        }

        if (request.Status is SignatureStatus.Completed or SignatureStatus.Cancelled or SignatureStatus.Rejected)
        {
            _logger.LogInformation(
                "Skipping signing job {JobId}; signature {SignatureId} is already {Status}",
                message.JobId,
                message.SignatureId,
                request.Status);
            return;
        }

        var job = await _db.SigningJobs
            .SingleOrDefaultAsync(j => j.Id == message.JobId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
        {
            throw new PermanentSigningJobException($"Signing job {message.JobId} was not found.");
        }

        if (job.Status is SigningJobStatus.Completed or SigningJobStatus.Cancelled)
        {
            _logger.LogInformation(
                "Skipping signing job {JobId}; job status is {Status}",
                message.JobId,
                job.Status);
            return;
        }

        var acquired = await _jobLock
            .TryAcquireAsync(job.Id, LockDuration, cancellationToken)
            .ConfigureAwait(false);

        if (!acquired)
        {
            _logger.LogInformation(
                "Skipping signing job {JobId}; lock not acquired (duplicate delivery or active lock held by another worker)",
                message.JobId);
            return;
        }

        // Conditional UPDATE bypasses the change tracker; reload before domain transitions.
        await _db.Entry(job).ReloadAsync(cancellationToken).ConfigureAwait(false);

        if (request.Status is SignatureStatus.Queued or SignatureStatus.RetryScheduled)
        {
            request.MarkProcessing();
        }

        if (job.Status == SigningJobStatus.Locked)
        {
            job.MarkProcessing();
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var inputKey = StorageKey.Create(message.InputPath);
        await using var inputStream = await _fileStorage
            .OpenReadAsync(inputKey, cancellationToken)
            .ConfigureAwait(false);

        SigningCertificateSelector? selector = null;
        if (!string.IsNullOrWhiteSpace(message.CertificateThumbprint))
        {
            selector = SigningCertificateSelector.ByThumbprint(message.CertificateThumbprint);
        }

        SignatureCreationResult signed;
        try
        {
            signed = await _signatureCreation
                .SignAsync(
                    inputStream,
                    request.Format,
                    request.Profile,
                    request.SigningProvider,
                    selector,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UnsupportedSignatureProfileException ex)
        {
            await MarkFailedAsync(
                    request,
                    job,
                    ex.ErrorCode,
                    ex.Message,
                    ex,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new PermanentSigningJobException(ex.Message, ex);
        }
        catch (UnsupportedSignatureFormatException ex)
        {
            await MarkFailedAsync(
                    request,
                    job,
                    ex.ErrorCode,
                    ex.Message,
                    ex,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new PermanentSigningJobException(ex.Message, ex);
        }
        catch (NotImplementedException ex)
        {
            await MarkFailedAsync(
                    request,
                    job,
                    ErrorCode.Create("SIGNING_OPERATION_FAILED"),
                    "Signature creation service is not implemented yet.",
                    ex,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new PermanentSigningJobException(
                "Signature creation service is not implemented yet.",
                ex);
        }
        catch (PermanentSigningJobException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leave request/job non-terminal so bounded retries can re-process.
            _logger.LogError(
                ex,
                "Signing job {JobId} failed for signature {SignatureId}; allowing retry classification",
                message.JobId,
                message.SignatureId);
            throw;
        }

        await using (signed.Content)
        {
            // Never double-write when another worker already completed this signature.
            await _db.Entry(request).ReloadAsync(cancellationToken).ConfigureAwait(false);
            await _db.Entry(job).ReloadAsync(cancellationToken).ConfigureAwait(false);

            if (request.Status is SignatureStatus.Completed or SignatureStatus.Cancelled or SignatureStatus.Rejected
                || job.Status is SigningJobStatus.Completed or SigningJobStatus.Cancelled)
            {
                _logger.LogInformation(
                    "Skipping signed output write for job {JobId}; signature {SignatureId} is already {RequestStatus}/{JobStatus}",
                    message.JobId,
                    message.SignatureId,
                    request.Status,
                    job.Status);
                return;
            }

            var outputKey = SignatureStorageKeys.ForSigned(tenantId, request.Id, request.CreatedAt);
            var contentType = string.IsNullOrWhiteSpace(signed.ContentType)
                ? "application/octet-stream"
                : signed.ContentType;

            var metadata = await _fileStorage
                .SaveAsync(outputKey, signed.Content, contentType, cancellationToken)
                .ConfigureAwait(false);

            var outputFile = StoredFile.Create(
                storageKey: outputKey,
                originalFileName: SignedOutputFileName(request.Format),
                contentType: contentType,
                size: metadata.Size,
                sha256: metadata.Sha256);

            _db.StoredFiles.Add(outputFile);
            request.MarkCompleted(outputFile.Id);
            job.MarkCompleted();

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Completed signing job {JobId} for signature {SignatureId}",
                message.JobId,
                message.SignatureId);
        }
    }

    private async Task MarkFailedAsync(
        SignatureRequest request,
        SigningJob job,
        ErrorCode errorCode,
        string message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Signing job {JobId} failed for signature {SignatureId}: {Message}",
            job.Id,
            request.Id,
            message);

        if (request.Status is SignatureStatus.Processing or SignatureStatus.Queued or SignatureStatus.RetryScheduled)
        {
            request.MarkFailed(errorCode, message);
        }

        if (job.Status is SigningJobStatus.Processing or SigningJobStatus.Locked)
        {
            job.MarkFailed(message);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string SignedOutputFileName(SignatureFormat format) => format switch
    {
        SignatureFormat.PAdES => "signed.pdf",
        SignatureFormat.XAdES => "signed.xml",
        SignatureFormat.CAdES => "signed.p7m",
        SignatureFormat.ASiC_S => "signed.asics",
        SignatureFormat.ASiC_E => "signed.asice",
        _ => "signed.bin"
    };
}
