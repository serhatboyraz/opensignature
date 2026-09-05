using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;

namespace OpenSignature.Domain.Entities;

public sealed class OutboxMessage
{
    private OutboxMessage(
        Guid id,
        string type,
        string payload,
        DateTimeOffset occurredAt,
        DateTimeOffset? publishedAt,
        int retryCount,
        string? error,
        OutboxMessageStatus status)
    {
        Id = id;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
        PublishedAt = publishedAt;
        RetryCount = retryCount;
        Error = error;
        Status = status;
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; }

    public string Payload { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int RetryCount { get; private set; }

    public string? Error { get; private set; }

    public OutboxMessageStatus Status { get; private set; }

    public static OutboxMessage Create(
        string type,
        string payload,
        DateTimeOffset? occurredAt = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Outbox message type must not be empty.", nameof(type));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Outbox message payload must not be empty.", nameof(payload));
        }

        return new OutboxMessage(
            id: Guid.CreateVersion7(),
            type: type.Trim(),
            payload: payload,
            occurredAt: occurredAt ?? DateTimeOffset.UtcNow,
            publishedAt: null,
            retryCount: 0,
            error: null,
            status: OutboxMessageStatus.Pending);
    }

    public void MarkProcessed(DateTimeOffset? publishedAt = null)
    {
        if (Status != OutboxMessageStatus.Pending && Status != OutboxMessageStatus.Failed)
        {
            throw new DomainException($"Cannot mark outbox message as processed from status {Status}.");
        }

        Status = OutboxMessageStatus.Processed;
        PublishedAt = publishedAt ?? DateTimeOffset.UtcNow;
        Error = null;
    }

    public void MarkFailed(string? error = null)
    {
        if (Status == OutboxMessageStatus.Processed)
        {
            throw new DomainException("Cannot fail an already processed outbox message.");
        }

        Status = OutboxMessageStatus.Failed;
        RetryCount++;
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }
}
