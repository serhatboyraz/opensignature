using System.Buffers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Storage;
using OpenSignature.Application.Abstractions.Verification;
using OpenSignature.Application.Signatures;
using OpenSignature.Application.Verification;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Validation.Reports;
using OpenSignature.Validation.Signatures;

namespace OpenSignature.Infrastructure.Verification;

/// <summary>
/// Loads stored or uploaded signed bytes and produces a detailed verification report.
/// Does not sign, persist uploads, or expose private keys.
/// </summary>
public sealed class SignatureVerificationService : ISignatureVerificationService
{
    private const int CopyBufferSize = 81920;

    private readonly OpenSignatureDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ISignatureValidator _signatureValidator;
    private readonly IValidationReportBuilder _reportBuilder;
    private readonly IOptions<SignatureApiOptions> _options;
    private readonly ILogger<SignatureVerificationService> _logger;

    public SignatureVerificationService(
        OpenSignatureDbContext db,
        IFileStorage fileStorage,
        ISignatureValidator signatureValidator,
        IValidationReportBuilder reportBuilder,
        IOptions<SignatureApiOptions> options,
        ILogger<SignatureVerificationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        _signatureValidator = signatureValidator ?? throw new ArgumentNullException(nameof(signatureValidator));
        _reportBuilder = reportBuilder ?? throw new ArgumentNullException(nameof(reportBuilder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SignatureVerificationOutcome> VerifyStoredAsync(
        TenantId tenantId,
        Guid signatureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        if (signatureId == Guid.Empty)
        {
            return new SignatureVerificationOutcome.InvalidRequest(
                "SIGNATURE_REQUEST_INVALID",
                "Signature ID must not be empty.");
        }

        var request = await _db.SignatureRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == signatureId && r.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return SignatureVerificationOutcome.NotFound.Instance;
        }

        if (request.Status != SignatureStatus.Completed || request.OutputFileId is null)
        {
            return new SignatureVerificationOutcome.NotReady(
                "SIGNATURE_OUTPUT_NOT_FOUND",
                "Verification is available only when the signature request status is Completed.");
        }

        var outputFile = await _db.StoredFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == request.OutputFileId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (outputFile is null || outputFile.IsDeleted)
        {
            return new SignatureVerificationOutcome.NotReady(
                "SIGNATURE_OUTPUT_NOT_FOUND",
                "Signed content metadata was not found.");
        }

        byte[] signedBytes;
        try
        {
            await using var signedStream = await _fileStorage
                .OpenReadAsync(outputFile.StorageKey, cancellationToken)
                .ConfigureAwait(false);
            signedBytes = await ReadAllBytesAsync(signedStream, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return new SignatureVerificationOutcome.NotReady(
                "SIGNATURE_OUTPUT_NOT_FOUND",
                "Signed content was not found in storage.");
        }

        if (signedBytes.Length == 0)
        {
            return new SignatureVerificationOutcome.NotReady(
                "SIGNATURE_OUTPUT_NOT_FOUND",
                "Signed content is empty.");
        }

        // Platform CAdES is attached (encapsulated content). Do not pass the original input.
        var report = await ValidateAsync(
                request.Format,
                signedBytes,
                originalBytes: null,
                VerificationReportSource.StoredSignature,
                request.Id,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Verified stored signature {SignatureId} for tenant {TenantId}: {OverallStatus}",
            signatureId,
            tenantId.Value,
            report.OverallStatus);

        return new SignatureVerificationOutcome.Success(report);
    }

    /// <inheritdoc />
    public async Task<SignatureVerificationOutcome> VerifyUploadedAsync(
        VerifyUploadedSignatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.SignedContent);

        var maxBytes = _options.Value.MaxUploadBytes;

        byte[] signedBytes;
        try
        {
            signedBytes = await ReadLimitedBytesAsync(command.SignedContent, maxBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UploadTooLargeException ex)
        {
            return new SignatureVerificationOutcome.TooLarge("SIGNATURE_REQUEST_INVALID", ex.Message);
        }

        if (signedBytes.Length == 0)
        {
            return new SignatureVerificationOutcome.InvalidRequest(
                "SIGNATURE_REQUEST_INVALID",
                "A non-empty signed 'file' is required.");
        }

        byte[]? originalBytes = null;
        if (command.OriginalContent is not null)
        {
            try
            {
                originalBytes = await ReadLimitedBytesAsync(command.OriginalContent, maxBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UploadTooLargeException ex)
            {
                return new SignatureVerificationOutcome.TooLarge("SIGNATURE_REQUEST_INVALID", ex.Message);
            }

            if (originalBytes.Length == 0)
            {
                originalBytes = null;
            }
        }

        var report = await ValidateAsync(
                command.Format,
                signedBytes,
                originalBytes,
                VerificationReportSource.UploadedDocument,
                signatureId: null,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Verified uploaded {Format} document: {OverallStatus}",
            command.Format,
            report.OverallStatus);

        return new SignatureVerificationOutcome.Success(report);
    }

    private async Task<SignatureVerificationReportDto> ValidateAsync(
        SignatureFormat format,
        byte[] signedBytes,
        byte[]? originalBytes,
        string source,
        Guid? signatureId,
        CancellationToken cancellationToken)
    {
        var validationRequest = new SignatureValidationRequest(format, signedBytes, originalBytes);
        var result = await _signatureValidator
            .ValidateAsync(validationRequest, cancellationToken)
            .ConfigureAwait(false);
        var report = _reportBuilder.Build(result);
        return MapReport(report, source, signatureId);
    }

    private static SignatureVerificationReportDto MapReport(
        ValidationReport report,
        string source,
        Guid? signatureId)
    {
        return new SignatureVerificationReportDto(
            OverallStatus: report.OverallStatus,
            IsValid: report.IsValid,
            ReasonCodes: report.ReasonCodes,
            CheckedAt: report.CheckedAt,
            Source: source,
            Format: report.Format?.ToString(),
            SignatureId: signatureId,
            Detail: report.Detail,
            Limitations: VerificationReportLimitations.BaselineB,
            Signature: report.Signature is null
                ? null
                : new SignatureCryptoCheckDto(
                    report.Signature.CryptoValid,
                    report.Signature.SignerThumbprint,
                    report.Signature.SignerSubject,
                    report.Signature.ReasonCodes),
            Certificate: report.Certificate is null
                ? null
                : new CertificatePathReportDto(
                    report.Certificate.IsValid,
                    report.Certificate.Subject,
                    report.Certificate.Issuer,
                    report.Certificate.Thumbprint,
                    report.Certificate.NotBefore,
                    report.Certificate.NotAfter,
                    report.Certificate.ReasonCodes,
                    report.Certificate.ChainStatus,
                    report.Certificate.Revocation is null
                        ? null
                        : new RevocationReportDto(
                            report.Certificate.Revocation.Status,
                            report.Certificate.Revocation.Source,
                            report.Certificate.Revocation.Detail)));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new UploadTooLargeException(
                        $"Uploaded file exceeds the maximum size of {maxBytes} bytes.");
                }

                await buffer.WriteAsync(rented.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return buffer.ToArray();
    }

    private sealed class UploadTooLargeException : Exception
    {
        public UploadTooLargeException(string message)
            : base(message)
        {
        }
    }
}
