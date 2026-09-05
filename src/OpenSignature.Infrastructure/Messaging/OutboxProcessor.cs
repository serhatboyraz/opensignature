using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Infrastructure.Persistence;

namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Publishes pending / retryable outbox rows via <see cref="ISigningJobPublisher"/>.
/// </summary>
public sealed class OutboxProcessor : IOutboxProcessor
{
    private const int MaxErrorLength = 2048;

    private readonly OpenSignatureDbContext _dbContext;
    private readonly ISigningJobPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        OpenSignatureDbContext dbContext,
        ISigningJobPublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Max(1, _options.BatchSize);
        var maxRetries = Math.Max(1, _options.MaxRetries);

        var messages = await _dbContext.OutboxMessages
            .Where(m =>
                (m.Status == OutboxMessageStatus.Pending || m.Status == OutboxMessageStatus.Failed)
                && m.RetryCount < maxRetries)
            .OrderBy(m => m.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            await PublishOneAsync(message, maxRetries, cancellationToken).ConfigureAwait(false);
        }

        return messages.Count;
    }

    private async Task PublishOneAsync(
        OutboxMessage message,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = SigningJobMessageJson.Deserialize(message.Payload);
            if (job is null)
            {
                message.MarkFailed(TruncateError("Outbox payload could not be deserialized as SigningJobMessage."));
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                LogExhaustedIfNeeded(message, maxRetries);
                return;
            }

            await _publisher.PublishAsync(job, cancellationToken).ConfigureAwait(false);
            message.MarkProcessed();
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Outbox message {OutboxMessageId} of type {OutboxType} published for job {JobId}",
                message.Id,
                message.Type,
                job.JobId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            message.MarkFailed(TruncateError(ex.Message));
            await _dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            _logger.LogWarning(
                ex,
                "Outbox message {OutboxMessageId} publish failed (retryCount {RetryCount})",
                message.Id,
                message.RetryCount);

            LogExhaustedIfNeeded(message, maxRetries);
        }
    }

    private void LogExhaustedIfNeeded(OutboxMessage message, int maxRetries)
    {
        if (message.RetryCount < maxRetries)
        {
            return;
        }

        _logger.LogError(
            "Outbox message {OutboxMessageId} exhausted retries ({RetryCount}/{MaxRetries}): {Error}",
            message.Id,
            message.RetryCount,
            maxRetries,
            message.Error);
    }

    private static string? TruncateError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var trimmed = error.Trim();
        return trimmed.Length <= MaxErrorLength
            ? trimmed
            : trimmed[..MaxErrorLength];
    }
}
